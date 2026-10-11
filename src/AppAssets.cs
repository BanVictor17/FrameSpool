using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace FrameSpool
{
    internal static class AppIdentity
    {
        public const string Name = "FrameSpool";
        public static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);
    }

    // All built-in data lives in the executable. No installed photo application,
    // working-directory assets or files alongside the executable are consulted.
    internal static class AppAssets
    {
        public const string ResourceName = "FrameSpool.Assets.zip";
        private static readonly Lazy<Dictionary<string, byte[]>> Files =
            new Lazy<Dictionary<string, byte[]>>(Load);

        private static Dictionary<string, byte[]> Load()
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (resource == null) throw new InvalidDataException("Built-in assets are missing. Rebuild with build.ps1.");
                using (var archive = new ZipArchive(resource, ZipArchiveMode.Read))
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (String.IsNullOrEmpty(entry.Name)) continue;
                        using (Stream source = entry.Open())
                        using (var output = new MemoryStream())
                        {
                            source.CopyTo(output);
                            files.Add(Normalize(entry.FullName), output.ToArray());
                        }
                    }
            }
            return files;
        }

        private static string Normalize(string path) { return path.Replace('\\', '/'); }
        public static bool Contains(string path) { return !String.IsNullOrEmpty(path) && Files.Value.ContainsKey(Normalize(path)); }
        public static Stream Open(string path)
        {
            byte[] bytes;
            if (!Files.Value.TryGetValue(Normalize(path), out bytes)) throw new FileNotFoundException("Built-in asset is missing.", path);
            return new MemoryStream(bytes, false);
        }
        public static string ReadText(string path) { using (var reader = new StreamReader(Open(path))) return reader.ReadToEnd(); }
        public static Icon CreateIcon()
        {
            using (Stream stream = Open("application.ico"))
            using (var icon = new Icon(stream)) return (Icon)icon.Clone();
        }
    }
}
