using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FrameSpool
{
    internal sealed class RawPhotoInfo
    {
        public int Width, Height;
        public DateTime? CaptureDate;
        public Dictionary<string, string> Metadata = new Dictionary<string, string>();
        public Dictionary<string, double[]> WhiteBalances = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
    }

    // LibRaw runs in a separate, hidden process. Its unmodified, statically linked
    // programs are embedded in FrameSpool; no camera codec or vendor app is needed.
    internal static class RawDecoder
    {
        public const string Version = "0.22.2";
        private const long MaximumInputBytes = 1073741824;
        private const long MaximumPixels = 100000000;
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, RawPhotoInfo> InfoCache = new Dictionary<string, RawPhotoInfo>();
        private static readonly string CacheDirectory = Path.Combine(AppIdentity.DataDirectory, "raw-cache");
        private static readonly string[] RawExtensions = {
            ".3fr", ".arw", ".bay", ".cap", ".cr2", ".cr3", ".crw", ".dcr", ".dcs", ".dng", ".erf", ".fff",
            ".iiq", ".k25", ".kdc", ".mdc", ".mef", ".mos", ".mrw", ".nef", ".nrw", ".obm", ".orf", ".pef",
            ".ptx", ".pxn", ".raf", ".raw", ".rw2", ".rwl", ".rwz", ".sr2", ".srf", ".srw", ".sti", ".x3f"
        };

        public static bool IsRawPath(string path)
        {
            if (String.IsNullOrEmpty(path)) return false;
            string extension = Path.GetExtension(path);
            return Array.Exists(RawExtensions, delegate(string value) { return String.Equals(extension, value, StringComparison.OrdinalIgnoreCase); });
        }

        public static RawPhotoInfo Identify(string path)
        {
            RequireArchitecture();
            string key = Fingerprint(path);
            lock (Sync)
            {
                RawPhotoInfo cached;
                if (InfoCache.TryGetValue(key, out cached)) return cached;
                string directory = CreateJobDirectory();
                try
                {
                    string input = CopyInput(path, directory);
                    string report = RunTool("raw-identify.exe", "-v " + Quote(input), directory, 30000);
                    RawPhotoInfo info = ParseInfo(report);
                    if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaximumPixels)
                        throw new InvalidDataException("This RAW photo is invalid or exceeds the 100-megapixel decoding limit.");
                    if (InfoCache.Count >= 1000) InfoCache.Clear();
                    InfoCache[key] = info;
                    return info;
                }
                finally { DeleteJob(directory); }
            }
        }

        public static Bitmap Load(string path, PhotoAdjustments adjustments)
        { return LoadCore(path, adjustments, false); }

        public static Bitmap LoadPreview(string path)
        { return LoadCore(path, null, true); }

        private static Bitmap LoadCore(string path, PhotoAdjustments adjustments, bool preview)
        {
            RequireArchitecture();
            RawPhotoInfo info = Identify(path);
            int exposure = adjustments == null ? 0 : Math.Max(-6, Math.Min(6, adjustments.RawExposureSteps));
            string whiteBalance = adjustments == null || adjustments.Monochrome ? "As Shot" : NormalizeWhiteBalance(adjustments.RawWhiteBalance);
            string key = Hash(Encoding.UTF8.GetBytes(Fingerprint(path) + "|" + exposure + "|" + whiteBalance + "|" + preview));
            lock (Sync)
            {
                Directory.CreateDirectory(CacheDirectory);
                string cached = Path.Combine(CacheDirectory, key + ".tif");
                if (!File.Exists(cached))
                {
                    string directory = CreateJobDirectory();
                    try
                    {
                        string input = CopyInput(path, directory);
                        string output = Path.Combine(directory, "developed.tif");
                        // LibRaw converts the camera matrix into sRGB. Camera orientation
                        // is applied by the decoder, so the bitmap is already oriented.
                        string arguments = "-T -o 1 -q 3 -H 2 -W " + (preview ? "-h " : "") + WhiteBalanceArguments(info, whiteBalance) +
                            " -b " + Math.Pow(2, exposure * .5).ToString("0.########", CultureInfo.InvariantCulture) +
                            " -Z developed.tif " + Quote(input);
                        RunTool("dcraw_emu.exe", arguments, directory, 120000);
                        if (!File.Exists(output) || new FileInfo(output).Length > MaximumPixels * 4 + 1048576)
                            throw new InvalidDataException("The RAW decoder did not produce a valid photo.");
                        using (Image check = Image.FromFile(output))
                            if (!preview && (check.Width != info.Width || check.Height != info.Height) ||
                                preview && (check.Width <= 0 || check.Height <= 0 || check.Width > info.Width || check.Height > info.Height))
                                throw new InvalidDataException("The RAW decoder returned unexpected photo dimensions.");
                        try { File.Move(output, cached); }
                        catch (IOException) { if (!File.Exists(cached)) throw; }
                        PruneCache(cached);
                    }
                    finally { DeleteJob(directory); }
                }
                File.SetLastAccessTimeUtc(cached, DateTime.UtcNow);
                return PhotoEngine.LoadImage(cached);
            }
        }

        internal static RawPhotoInfo ParseInfo(string report)
        {
            RawPhotoInfo info = new RawPhotoInfo();
            Match size = Regex.Match(report, @"(?m)^Output size:\s*(\d+)\s*x\s*(\d+)\s*$");
            if (!size.Success) throw new InvalidDataException("This camera RAW format is not supported by the bundled decoder.");
            info.Width = Int32.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
            info.Height = Int32.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture);
            // raw-identify calls adjust_sizes_info_only before reporting Output size;
            // these dimensions already include the camera's rotation.
            string timestamp = Regex.Replace(Line(report, "Timestamp"), @"\s+", " ");
            DateTime date;
            if (DateTime.TryParseExact(timestamp, "ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date.Year > 1970)
                info.CaptureDate = date;
            info.Metadata["File Type"] = "Camera RAW (LibRaw " + Version + ")";
            info.Metadata["Camera"] = Regex.Replace(Line(report, "Camera"), @"\s+ID:.*$", "");
            info.Metadata["EXP Time"] = Line(report, "Shutter");
            info.Metadata["F Number"] = Line(report, "Aperture");
            info.Metadata["ISO Sensitivity"] = Line(report, "ISO speed");
            info.Metadata["Flash"] = "-";
            info.Metadata["Exif/DCF Color Space"] = "Developed into sRGB";
            info.Metadata["Mono. Info"] = "-";
            info.Metadata["Color Management"] = "LibRaw camera color matrix to sRGB";
            foreach (Match match in Regex.Matches(report, @"(?m)^\s{2}(As shot|Daylight|Fine Weather|Cloudy|Shade|Fluorescent|Tungsten \(Incandescent\)|Flash)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)"))
            {
                var multipliers = new double[4];
                for (int i = 0; i < 4; i++) multipliers[i] = Double.Parse(match.Groups[i + 2].Value, CultureInfo.InvariantCulture);
                if (multipliers[0] <= 0 || multipliers[1] <= 0 || multipliers[2] <= 0) continue;
                if (multipliers[3] <= 0) multipliers[3] = multipliers[1];
                string name = match.Groups[1].Value;
                if (name == "Tungsten (Incandescent)") name = "Incandescent";
                else if (name == "Fine Weather") name = "Daylight";
                info.WhiteBalances[name] = multipliers;
            }
            info.Metadata["RAW White Balance"] = info.WhiteBalances.Count > 1 ? "Camera presets when present; temperature fallback otherwise" : "As shot; temperature fallback for presets";
            return info;
        }

        private static string Line(string report, string name)
        {
            Match value = Regex.Match(report, "(?m)^" + Regex.Escape(name) + @":\s*([^\r\n]*)");
            return value.Success ? value.Groups[1].Value.Trim() : "-";
        }

        internal static string NormalizeWhiteBalance(string value)
        {
            foreach (string preset in new[] { "As Shot", "Daylight", "Cloudy", "Shade", "Fluorescent", "Incandescent", "Flash" })
                if (String.Equals(value, preset, StringComparison.OrdinalIgnoreCase)) return preset;
            return "As Shot";
        }

        private static string WhiteBalanceArguments(RawPhotoInfo info, string preset)
        {
            if (preset == "As Shot") return "-w";
            double[] values;
            if (!info.WhiteBalances.TryGetValue(preset, out values))
            {
                // Independent temperature approximation relative to the camera's as-shot
                // multipliers. These are not replacements for missing camera WB tables.
                double[] asShot;
                if (!info.WhiteBalances.TryGetValue("As shot", out asShot)) asShot = new[] { 2.0, 1.0, 1.5, 1.0 };
                double red = 1, blue = 1;
                switch (preset)
                {
                    case "Cloudy": red = 1.10; blue = .90; break;
                    case "Shade": red = 1.20; blue = .80; break;
                    case "Fluorescent": red = .86; blue = 1.18; break;
                    case "Incandescent": red = .65; blue = 1.55; break;
                    case "Flash": red = 1.04; blue = .96; break;
                }
                values = new[] { asShot[0] * red, asShot[1], asShot[2] * blue, asShot[3] };
            }
            var arguments = new StringBuilder("-r");
            foreach (double value in values) arguments.Append(' ').Append(value.ToString("0.########", CultureInfo.InvariantCulture));
            return arguments.ToString();
        }

        private static void RequireArchitecture()
        {
            if (!Environment.Is64BitOperatingSystem)
                throw new PlatformNotSupportedException("Camera RAW decoding requires 64-bit Windows.");
        }

        private static string Fingerprint(string path)
        {
            FileInfo file = new FileInfo(Path.GetFullPath(path));
            if (!file.Exists) throw new FileNotFoundException("Cannot find the photo.", path);
            if (file.Length <= 0 || file.Length > MaximumInputBytes)
                throw new InvalidDataException("RAW files must be smaller than 1 GB.");
            return Hash(Encoding.UTF8.GetBytes(Version + "|" + file.FullName.ToUpperInvariant() + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks));
        }

        private static string CreateJobDirectory()
        {
            string path = Path.Combine(CacheDirectory, "job-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string CopyInput(string path, string directory)
        {
            string fullPath = Path.GetFullPath(path);
            bool ascii = fullPath.Length < 240;
            foreach (char character in fullPath) if (character > 127) { ascii = false; break; }
            if (ascii) return fullPath;
            // The upstream samples use narrow argv. ASCII names avoid failures on
            // Unicode photo paths without touching or renaming the source file.
            string filename = "input" + Path.GetExtension(path).ToLowerInvariant();
            File.Copy(path, Path.Combine(directory, filename));
            return filename;
        }

        private static string ExtractTool(string filename)
        {
            byte[] data;
            using (Stream stream = AppRuntime.Open("libraw/" + filename))
            using (var memory = new MemoryStream()) { stream.CopyTo(memory); data = memory.ToArray(); }
            string digest = Hash(data);
            string directory = Path.Combine(AppIdentity.DataDirectory, "runtime", "libraw-" + Version + "-" + digest.Substring(0, 16));
            Directory.CreateDirectory(directory);
            string output = Path.Combine(directory, filename);
            if (File.Exists(output))
            {
                using (var sha = SHA256.Create())
                using (var file = File.OpenRead(output))
                    if (Hex(sha.ComputeHash(file)) == digest) return output;
                throw new InvalidDataException("The cached RAW decoder was modified. Remove its runtime cache and try again.");
            }
            string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, data);
            try { File.Move(temporary, output); }
            catch (IOException)
            {
                if (!File.Exists(output)) throw;
                using (var sha = SHA256.Create())
                using (var file = File.OpenRead(output))
                    if (Hex(sha.ComputeHash(file)) != digest) throw new InvalidDataException("The RAW decoder cache is invalid.");
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return output;
        }

        private static string RunTool(string filename, string arguments, string directory, int timeout)
        {
            var output = new StringBuilder();
            var error = new StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(ExtractTool(filename), arguments) {
                    WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true
                };
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args) { if (args.Data != null) lock (output) if (output.Length < 65536) output.AppendLine(args.Data); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args) { if (args.Data != null) lock (error) if (error.Length < 8192) error.AppendLine(args.Data); };
                if (!process.Start()) throw new InvalidOperationException("The RAW decoder could not start.");
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                if (!process.WaitForExit(timeout))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    process.WaitForExit();
                    throw new TimeoutException("RAW decoding took too long. Try a smaller file or another RAW format.");
                }
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidDataException("The camera RAW file could not be decoded. " + error.ToString().Trim());
                return output.ToString();
            }
        }

        private static void PruneCache(string keep)
        {
            var files = new List<FileInfo>(new DirectoryInfo(CacheDirectory).GetFiles("*.tif"));
            files.Sort(delegate(FileInfo a, FileInfo b) { return b.LastAccessTimeUtc.CompareTo(a.LastAccessTimeUtc); });
            long bytes = 0;
            foreach (var file in files)
            {
                bytes += file.Length;
                if (bytes <= 536870912 || String.Equals(file.FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
                try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static void DeleteJob(string directory)
        {
            // Only directories created by this decoder under its fixed cache root.
            if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(CacheDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        private static string Quote(string value) { return "\"" + value.Replace("\"", "") + "\""; }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }
}
