using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace XFiles.FileSystem
{
    /// <summary>
    /// Test-only stubs for the FileOperations methods referenced by
    /// NetworkCopyService. Lets us link NetworkCopyService.cs into the
    /// net8.0 test project without pulling the full FileOperations.cs
    /// (which depends on SharpCompress + P/Invoke).
    /// </summary>
    partial class FileOperations
    {
        public static async Task<(List<string> entries, int folderCount)> ListRecursiveAsync(string path)
        {
            return await Task.Run(() =>
            {
                var result = new List<string>();
                int folderCount = 0;
                foreach (var d in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
                {
                    result.Add(d + Path.DirectorySeparatorChar);
                    folderCount++;
                }
                foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    result.Add(f);
                return (result, folderCount);
            });
        }

        public static string GetUniqueFilePath(string path)
        {
            if (File.Exists(path))
            {
                string dir = Path.GetDirectoryName(path);
                string nameNoExt = Path.GetFileNameWithoutExtension(path);
                string ext = Path.GetExtension(path);
                for (int i = 1; i < 10000; i++)
                {
                    string candidate = Path.Combine(dir, $"{nameNoExt} ({i}){ext}");
                    if (!File.Exists(candidate)) return candidate;
                }
            }
            return path;
        }

        public static string GetUniqueDirectoryPath(string path)
        {
            if (Directory.Exists(path))
            {
                string parent = Path.GetDirectoryName(path);
                string name = Path.GetFileName(path);
                for (int i = 1; i < 10000; i++)
                {
                    string candidate = Path.Combine(parent, $"{name} ({i})");
                    if (!Directory.Exists(candidate)) return candidate;
                }
            }
            return path;
        }
    }
}
