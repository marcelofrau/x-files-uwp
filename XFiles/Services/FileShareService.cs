using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using XFiles.FileSystem;

namespace XFiles.Services
{
    /// <summary>
    /// Upload files/folders to gofile.io for sharing via QR code.
    /// Single files: streamed directly from disk (no memory copy).
    /// Folders: zipped to temp file via FileOperations.CreateZipAsync, then streamed.
    /// </summary>
    internal static class FileShareService
    {
        private static readonly HttpClient SharedClient = new HttpClient();

        /// <summary>
        /// Share a file or folder by uploading to gofile.io.
        /// </summary>
        /// <param name="path">Full path to file or folder</param>
        /// <param name="statusCallback">Called with status text updates (e.g. "Getting server...")</param>
        /// <param name="progressCallback">Called with (bytesUploaded, totalBytes) during upload</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Download URL from gofile, or null on failure</returns>
        public static async Task<string> ShareAsync(
            string path,
            Action<string> statusCallback,
            Action<long, long> progressCallback,
            CancellationToken token)
        {
            string tempZipPath = null;
            try
            {
                // Determine what we're sharing
                string pathType = FileOperations.CheckPathType(path);
                bool isFolder = pathType == "directory";

                statusCallback?.Invoke("Getting server...");
                Log.Info("FileShareService.ShareAsync: {Path} (isFolder={IsFolder})", path, isFolder);

                // 1. Get gofile server
                string server = await GetServerAsync(token);
                if (string.IsNullOrEmpty(server))
                {
                    Log.Warn("FileShareService: failed to get server");
                    return null;
                }
                Log.Info("FileShareService: using server {Server}", server);

                // 2. Prepare file to upload
                string uploadFilePath;
                string uploadFileName;
                long fileSize;

                if (isFolder)
                {
                    statusCallback?.Invoke("Compressing...");

                    // Zip to temp file on disk
                    tempZipPath = GetTempZipPath();
                    Log.Info("FileShareService: zipping folder to {Temp}", tempZipPath);

                    var zipResult = await FileOperations.CreateZipAsync(path, tempZipPath, token: token);
                    if (zipResult != FileOperations.OperationResult.Success)
                    {
                        Log.Warn("FileShareService: zip failed ({Result})", zipResult);
                        return null;
                    }

                    uploadFilePath = tempZipPath;
                    string folderName = Path.GetFileName(path.TrimEnd('\\', '/'));
                    uploadFileName = $"{folderName}.zip";
                    fileSize = GetFileSize(tempZipPath);
                }
                else
                {
                    // Single file: stream directly from original
                    uploadFilePath = path;
                    uploadFileName = Path.GetFileName(path);
                    fileSize = GetFileSize(path);
                }

                Log.Info("FileShareService: uploading {File} ({Size} bytes)", uploadFileName, fileSize);

                // 3. Upload with streaming progress
                statusCallback?.Invoke($"Uploading {FormatBytes(fileSize)}...");
                string responseBody = await UploadFileAsync(
                    server, uploadFilePath, uploadFileName, fileSize,
                    progressCallback, token);

                if (string.IsNullOrEmpty(responseBody))
                {
                    Log.Warn("FileShareService: upload returned empty response");
                    return null;
                }

                // 4. Parse URL from response
                string url = ExtractUrlFromResponse(responseBody);
                Log.Info("FileShareService: download URL = {Url}", url ?? "(null)");
                return url;
            }
            catch (OperationCanceledException)
            {
                Log.Info("FileShareService: upload cancelled");
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn("FileShareService: exception: {Error}", ex.Message);
                if (ex.InnerException != null)
                    Log.Warn("FileShareService: inner: {Inner}", ex.InnerException.Message);
                return null;
            }
            finally
            {
                // Clean up temp zip file
                if (tempZipPath != null)
                {
                    try { DeleteFile(tempZipPath); }
                    catch { }
                }
            }
        }

        private static async Task<string> GetServerAsync(CancellationToken token)
        {
            try
            {
                var resp = await SharedClient.GetAsync("https://api.gofile.io/servers", token);
                string json = await resp.Content.ReadAsStringAsync();
                var obj = JsonObject.Parse(json);
                var data = obj.GetNamedObject("data");
                var servers = data.GetNamedArray("servers");
                return servers.GetObjectAt(0).GetNamedString("name");
            }
            catch (Exception ex)
            {
                Log.Warn("FileShareService.GetServerAsync: {Error}", ex.Message);
                return null;
            }
        }

        private static async Task<string> UploadFileAsync(
            string server,
            string filePath,
            string fileName,
            long fileSize,
            Action<long, long> progressCallback,
            CancellationToken token)
        {
            string uploadUrl = $"https://{server}.gofile.io/contents/uploadfile";

            using (var fileStream = Win32FileStream.OpenRead(filePath))
            {
                long totalBytes = fileSize;
                long bytesUploaded = 0;

                // Wrap stream to track upload progress
                var progressStream = new ProgressStream(fileStream, bytesRead =>
                {
                    Interlocked.Add(ref bytesUploaded, bytesRead);
                    progressCallback?.Invoke(bytesUploaded, totalBytes);
                });

                var streamContent = new StreamContent(progressStream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                using (var form = new MultipartFormDataContent())
                {
                    form.Add(streamContent, "file", fileName);

                    var resp = await SharedClient.PostAsync(uploadUrl, form, token);
                    string responseBody = await resp.Content.ReadAsStringAsync();
                    Log.Info("FileShareService.UploadFileAsync: status={Status}", resp.StatusCode);
                    return responseBody;
                }
            }
        }

        private static string ExtractUrlFromResponse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var obj = JsonObject.Parse(json);

                // gofile: { "data": { "downloadPage": "https://..." } }
                if (obj.ContainsKey("data") && obj["data"].ValueType == JsonValueType.Object)
                {
                    var data = obj.GetNamedObject("data");
                    if (data.ContainsKey("downloadPage"))
                        return data.GetNamedString("downloadPage");
                }

                // fallback: litterbox { "url": "..." } or { "link": "..." }
                if (obj.ContainsKey("url") && obj["url"].ValueType != JsonValueType.Null)
                    return obj["url"].GetString();
                if (obj.ContainsKey("link") && obj["link"].ValueType != JsonValueType.Null)
                    return obj["link"].GetString();
            }
            catch { }

            if (json.StartsWith("http"))
                return json.Trim();
            return null;
        }

        private static string GetTempZipPath()
        {
            string tempDir = Path.Combine(ApplicationData.Current.LocalFolder.Path, "temp");
            try { Directory.CreateDirectory(tempDir); } catch { }
            return Path.Combine(tempDir, $"share-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        }

        private static long GetFileSize(string path)
        {
            try
            {
                using (var fs = Win32FileStream.OpenRead(path))
                    return fs.Length;
            }
            catch { }
            return 0;
        }

        private static void DeleteFile(string path)
        {
            try { File.Delete(path); }
            catch { }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
