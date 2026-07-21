using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml.Media.Imaging;

namespace XFiles.FileSystem
{
    public static class PdfPreviewService
    {
        private static readonly Dictionary<string, PdfDocument> _documentCache
            = new Dictionary<string, PdfDocument>();

        public class PdfPageResult
        {
            public WriteableBitmap Bitmap { get; set; }
            public int PageCount { get; set; }
            public int PageWidth { get; set; }
            public int PageHeight { get; set; }
            public string ErrorMessage { get; set; }
        }

        public static void ClearCache()
        {
            _documentCache.Clear();
            Log.Information("PdfPreviewService: document cache cleared");
        }

        private static async Task<PdfDocument> GetOrLoadDocumentAsync(string filePath)
        {
            if (_documentCache.TryGetValue(filePath, out var cached))
                return cached;

            var doc = await Task.Run(async () =>
            {
                using (var fs = Win32FileStream.OpenRead(filePath))
                {
                    if (fs == null)
                        throw new System.IO.FileNotFoundException(
                            $"Cannot open PDF: {filePath}");

                    using (var ims = new InMemoryRandomAccessStream())
                    {
                        using (var writer = new DataWriter(ims.GetOutputStreamAt(0)))
                        {
                            var buf = new byte[Math.Min(fs.Length, 1024 * 1024)];
                            int read;
                            while ((read = fs.Read(buf, 0, buf.Length)) > 0)
                                writer.WriteBytes(buf, 0, read);
                            await writer.StoreAsync();
                            await writer.FlushAsync();
                        }
                        ims.Seek(0);
                        return await PdfDocument.LoadFromStreamAsync(ims);
                    }
                }
            });

            // limit cache to 5 documents
            if (_documentCache.Count >= 5)
            {
                var enumerator = _documentCache.GetEnumerator();
                enumerator.MoveNext();
                _documentCache.Remove(enumerator.Current.Key);
            }

            _documentCache[filePath] = doc;
            return doc;
        }

        public static async Task<PdfPageResult> LoadPageAsync(
            string filePath, int pageIndex = 0, uint? targetWidth = null)
        {
            var result = new PdfPageResult();

            try
            {
                var pdfDoc = await GetOrLoadDocumentAsync(filePath);

                if (pdfDoc.PageCount == 0)
                {
                    result.ErrorMessage = "PDF has no pages";
                    return result;
                }

                result.PageCount = (int)pdfDoc.PageCount;

                int idx = Math.Max(0, Math.Min(pageIndex, (int)pdfDoc.PageCount - 1));
                var page = pdfDoc.GetPage((uint)idx);

                result.PageWidth = (int)page.Size.Width;
                result.PageHeight = (int)page.Size.Height;

                using (var ms = new InMemoryRandomAccessStream())
                {
                    if (targetWidth.HasValue)
                    {
                        var opts = new PdfPageRenderOptions
                        {
                            DestinationWidth = targetWidth.Value
                        };
                        await page.RenderToStreamAsync(ms, opts);
                    }
                    else
                    {
                        await page.RenderToStreamAsync(ms);
                    }

                    ms.Seek(0);

                    var decoder = await BitmapDecoder.CreateAsync(ms);
                    var sb = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

                    var dispatcher = CoreApplication.MainView.CoreWindow?.Dispatcher;
                    if (dispatcher != null)
                    {
                        var tcs = new TaskCompletionSource<bool>();
                        await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            try
                            {
                                var wb = new WriteableBitmap(
                                    (int)decoder.PixelWidth, (int)decoder.PixelHeight);
                                sb.CopyToBuffer(wb.PixelBuffer);
                                result.Bitmap = wb;
                                tcs.SetResult(true);
                            }
                            catch (Exception ex)
                            {
                                result.ErrorMessage = $"Bitmap error: {ex.Message}";
                                tcs.SetResult(false);
                            }
                        });
                        await tcs.Task;
                    }
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"PDF load error: {ex.Message}";
                Log.Warning("PdfPreviewService: error loading '{Path}': {Error}",
                    filePath, ex.Message);
            }

            return result;
        }
    }
}
