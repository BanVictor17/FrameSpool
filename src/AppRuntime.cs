using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Linq;

namespace FrameSpool
{
    internal static class AppRuntime
    {
        // Decoder programs are redistributed dependencies, separate from the
        // reference application's non-executable artwork and template data.
        public static Stream Open(string name)
        {
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("FrameSpool.Runtime.zip"))
            {
                if (resource == null) throw new FileNotFoundException("The bundled decoder data is missing. Rebuild FrameSpool.");
                using (var archive = new ZipArchive(resource, ZipArchiveMode.Read))
                {
                    var normalized=name.Replace('\\','/');
                    var entry = archive.Entries.FirstOrDefault(e=>e.FullName.Replace('\\','/')==normalized);
                    if (entry == null) throw new FileNotFoundException("The bundled runtime file is missing.", name);
                    var output = new MemoryStream();
                    using (Stream input = entry.Open()) input.CopyTo(output);
                    output.Position = 0; return output;
                }
            }
        }
    }
}
