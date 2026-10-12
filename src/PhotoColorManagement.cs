using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FrameSpool
{
    // The bundled engine works independently of Windows' installed profiles/CMM.
    internal static class PhotoColorManagement
    {
        private const string Library = "FrameSpool.LittleCMS";
        private const uint Bgra8 = (4u << 16) | (1u << 14) | (1u << 10) | (1u << 7) | (3u << 3) | 1u;
        private const uint CopyAlpha = 0x04000000;
        private static readonly object NativeSync = new object();
        private static IntPtr NativeHandle;

        static PhotoColorManagement()
        {
            NativeLibrary.SetDllImportResolver(typeof(PhotoColorManagement).Assembly, ResolveLibrary);
        }

        [DllImport(Library)] private static extern IntPtr cmsCreateContext(IntPtr plugins, IntPtr user);
        [DllImport(Library)] private static extern void cmsDeleteContext(IntPtr context);
        [DllImport(Library)] private static extern IntPtr cmsOpenProfileFromMemTHR(IntPtr context, byte[] data, uint size);
        [DllImport(Library)] private static extern IntPtr cmsCreate_sRGBProfileTHR(IntPtr context);
        [DllImport(Library)] private static extern uint cmsGetColorSpace(IntPtr profile);
        [DllImport(Library)] private static extern int cmsCloseProfile(IntPtr profile);
        [DllImport(Library)] private static extern IntPtr cmsCreateTransformTHR(IntPtr context, IntPtr input, uint inputFormat,
            IntPtr output, uint outputFormat, uint intent, uint flags);
        [DllImport(Library)] private static extern void cmsDeleteTransform(IntPtr transform);
        [DllImport(Library)] private static extern void cmsDoTransform(IntPtr transform, IntPtr source, IntPtr destination, uint pixels);

        private static IntPtr ResolveLibrary(string name, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (name != Library) return IntPtr.Zero;
            lock (NativeSync)
            {
                if (NativeHandle != IntPtr.Zero) return NativeHandle;
                byte[] data;
                using (var stream = AppRuntime.Open("lcms/lcms2.dll"))
                using (var memory = new MemoryStream()) { stream.CopyTo(memory); data = memory.ToArray(); }
                string digest = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                string directory = Path.Combine(AppIdentity.DataDirectory, "runtime", "lcms-2.19.1-" + digest.Substring(0, 16));
                Directory.CreateDirectory(directory);
                string filename = Path.Combine(directory, "lcms2.dll");
                if (!File.Exists(filename))
                {
                    string temporary = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllBytes(temporary, data);
                        try { File.Move(temporary, filename); }
                        catch (IOException) { if (!File.Exists(filename)) throw; }
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                using (var file = File.OpenRead(filename))
                    if (Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant() != digest)
                        throw new InvalidDataException("The cached color engine was modified. Remove its runtime cache and try again.");
                NativeHandle = NativeLibrary.Load(filename);
                return NativeHandle;
            }
        }

        internal static byte[] EmbeddedProfile(Image source)
        {
            try { return source.GetPropertyItem(0x8773).Value; }
            catch (ArgumentException) { return null; }
        }

        public static void ConvertToSrgb(Bitmap bitmap, byte[] profileBytes)
        {
            // Untagged images are interpreted as sRGB. Never silently discard a
            // tagged image's profile: its RGB values may belong to another space.
            if (profileBytes == null || profileBytes.Length == 0) return;
            if (profileBytes.Length < 132 || profileBytes.Length > 4194304 ||
                Encoding.ASCII.GetString(profileBytes, 36, 4) != "acsp" ||
                Encoding.ASCII.GetString(profileBytes, 16, 4) != "RGB ")
                throw ProfileError();
            IntPtr context = IntPtr.Zero, sourceProfile = IntPtr.Zero, destinationProfile = IntPtr.Zero,
                transform = IntPtr.Zero, row = IntPtr.Zero;
            BitmapData bits = null;
            try
            {
                // Separate contexts/transforms keep concurrent thumbnails and
                // full-size previews independent, including error state.
                context = cmsCreateContext(IntPtr.Zero, IntPtr.Zero);
                if (context == IntPtr.Zero) throw new OutOfMemoryException("Cannot allocate the color engine.");
                sourceProfile = cmsOpenProfileFromMemTHR(context, profileBytes, (uint)profileBytes.Length);
                if (sourceProfile == IntPtr.Zero || cmsGetColorSpace(sourceProfile) != 0x52474220) throw ProfileError();
                destinationProfile = cmsCreate_sRGBProfileTHR(context);
                if (destinationProfile == IntPtr.Zero) throw new OutOfMemoryException("Cannot allocate the sRGB profile.");
                transform = cmsCreateTransformTHR(context, sourceProfile, Bgra8, destinationProfile, Bgra8, 1, CopyAlpha);
                if (transform == IntPtr.Zero) throw ProfileError();
                int rowBytes = checked(bitmap.Width * 4);
                row = Marshal.AllocHGlobal(rowBytes);
                byte[] converted = new byte[rowBytes];
                bits = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                for (int y = 0; y < bitmap.Height; y++)
                {
                    IntPtr scan = IntPtr.Add(bits.Scan0, y * bits.Stride);
                    cmsDoTransform(transform, scan, row, (uint)bitmap.Width);
                    Marshal.Copy(row, converted, 0, rowBytes);
                    Marshal.Copy(converted, 0, scan, rowBytes);
                }
            }
            finally
            {
                if (bits != null) bitmap.UnlockBits(bits);
                if (row != IntPtr.Zero) Marshal.FreeHGlobal(row);
                if (transform != IntPtr.Zero) cmsDeleteTransform(transform);
                if (sourceProfile != IntPtr.Zero) cmsCloseProfile(sourceProfile);
                if (destinationProfile != IntPtr.Zero) cmsCloseProfile(destinationProfile);
                if (context != IntPtr.Zero) cmsDeleteContext(context);
            }
        }

        private static InvalidDataException ProfileError()
        {
            return new InvalidDataException("This photo's embedded ICC profile is damaged, unsupported, or is not an RGB profile. " +
                "Export the photo with a valid RGB profile or as sRGB and import it again.");
        }
    }
}
