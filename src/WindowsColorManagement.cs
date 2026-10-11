using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FrameSpool
{
    internal static class WindowsColorManagement
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Profile { public uint Type; public IntPtr Data; public uint Size; }
        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenColorProfileW(ref Profile profile, uint access, uint sharing, uint creation);
        [DllImport("mscms.dll", SetLastError = true)] private static extern bool CloseColorProfile(IntPtr profile);
        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetStandardColorSpaceProfileW(string machine, uint colorSpace, StringBuilder buffer, ref uint size);
        [DllImport("mscms.dll", SetLastError = true)]
        private static extern IntPtr CreateMultiProfileTransform(IntPtr[] profiles, uint count, uint[] intents, uint intentCount, uint flags, uint preferred);
        [DllImport("mscms.dll", SetLastError = true)] private static extern bool DeleteColorTransform(IntPtr transform);
        [DllImport("mscms.dll", SetLastError = true)]
        private static extern bool TranslateBitmapBits(IntPtr transform, IntPtr source, uint inputFormat, uint width, uint height,
            uint inputStride, IntPtr destination, uint outputFormat, uint outputStride, IntPtr callback, IntPtr user);

        internal static byte[] EmbeddedProfile(Image source)
        {
            try { return source.GetPropertyItem(0x8773).Value; }
            catch (ArgumentException) { return null; }
        }

        public static void ConvertToSrgb(Bitmap bitmap, byte[] profileBytes)
        {
            if (profileBytes == null || profileBytes.Length == 0) return;
            if (profileBytes.Length < 128 || profileBytes.Length > 4194304 ||
                Encoding.ASCII.GetString(profileBytes, 36, 4) != "acsp" ||
                Encoding.ASCII.GetString(profileBytes, 16, 4) != "RGB ")
                throw new InvalidDataException("The embedded ICC profile is invalid or is not an RGB profile. Save the photo as sRGB and import it again.");
            IntPtr sourceBytes = IntPtr.Zero, destinationName = IntPtr.Zero, sourceProfile = IntPtr.Zero,
                destinationProfile = IntPtr.Zero, transform = IntPtr.Zero, row = IntPtr.Zero;
            BitmapData bits = null;
            try
            {
                sourceBytes = Marshal.AllocHGlobal(profileBytes.Length);
                Marshal.Copy(profileBytes, 0, sourceBytes, profileBytes.Length);
                var source = new Profile { Type = 2, Data = sourceBytes, Size = (uint)profileBytes.Length };
                sourceProfile = OpenColorProfileW(ref source, 1, 1, 3);
                if (sourceProfile == IntPtr.Zero) throw ProfileError();
                var filename = new StringBuilder(1024); uint size = 2048;
                if (!GetStandardColorSpaceProfileW(null, 0x73524742, filename, ref size)) throw ProfileError();
                destinationName = Marshal.StringToHGlobalUni(filename.ToString());
                var destination = new Profile { Type = 1, Data = destinationName, Size = (uint)((filename.Length + 1) * 2) };
                destinationProfile = OpenColorProfileW(ref destination, 1, 1, 3);
                if (destinationProfile == IntPtr.Zero) throw ProfileError();
                transform = CreateMultiProfileTransform(new[] { sourceProfile, destinationProfile }, 2, new uint[] { 1 }, 1, 3, 0);
                if (transform == IntPtr.Zero) throw ProfileError();
                int rowBytes = checked(bitmap.Width * 4);
                row = Marshal.AllocHGlobal(rowBytes);
                byte[] original = new byte[rowBytes], converted = new byte[rowBytes];
                bits = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                for (int y = 0; y < bitmap.Height; y++)
                {
                    IntPtr scan = IntPtr.Add(bits.Scan0, y * bits.Stride);
                    Marshal.Copy(scan, original, 0, rowBytes);
                    // BM_xRGBQUADS is B,G,R,x. ICM ignores the last byte; restore
                    // the source alpha explicitly to preserve transparent PNG/TIFF.
                    if (!TranslateBitmapBits(transform, scan, 8, (uint)bitmap.Width, 1, (uint)rowBytes,
                        row, 8, (uint)rowBytes, IntPtr.Zero, IntPtr.Zero)) throw ProfileError();
                    Marshal.Copy(row, converted, 0, rowBytes);
                    for (int i = 3; i < rowBytes; i += 4) converted[i] = original[i];
                    Marshal.Copy(converted, 0, scan, rowBytes);
                }
            }
            finally
            {
                if (bits != null) bitmap.UnlockBits(bits);
                if (row != IntPtr.Zero) Marshal.FreeHGlobal(row);
                if (transform != IntPtr.Zero) DeleteColorTransform(transform);
                if (sourceProfile != IntPtr.Zero) CloseColorProfile(sourceProfile);
                if (destinationProfile != IntPtr.Zero) CloseColorProfile(destinationProfile);
                if (sourceBytes != IntPtr.Zero) Marshal.FreeHGlobal(sourceBytes);
                if (destinationName != IntPtr.Zero) Marshal.FreeHGlobal(destinationName);
            }
        }

        private static InvalidDataException ProfileError()
        {
            return new InvalidDataException("Windows could not convert this photo's embedded ICC profile into sRGB. " +
                "Save it as sRGB and import it again. (Color management error " + Marshal.GetLastWin32Error() + ")");
        }
    }
}
