using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FrameSpool
{
    internal static class PhotoDecodingVerification
    {
        public static void Run(string directory, Action<string, bool> check)
        {
            Directory.CreateDirectory(directory);
            string raw = Path.Combine(directory, "generated-RAW-ș-照片.dng");
            WriteDng(raw, 1);
            string digest = Digest(raw);
            var photo = new PhotoItem(raw);
            check("Generated Bayer DNG identified as RAW", photo.IsRaw && photo.Width == 128 && photo.Height == 96);
            check("RAW shot date and camera metadata read", photo.CaptureDate == new DateTime(2016, 7, 31, 17, 30, 9) && photo.Metadata["Camera"].Contains("Synthetic Bayer"));
            using (var baseline = PhotoEngine.RenderImage(photo, false))
            {
                Color red = baseline.GetPixel(24, 24), green = baseline.GetPixel(100, 24), blue = baseline.GetPixel(24, 72);
                check("RAW Bayer data fully developed, beyond embedded preview", red.R > red.G * 1.4 && green.G > green.R * 1.4 && blue.B > blue.R * 1.4);
                photo.Adjustments.Mode = "None";
                photo.Adjustments.RawExposureSteps = 2;
                using (var exposed = PhotoEngine.RenderImage(photo, true))
                    check("RAW exposure applies during development", exposed.GetPixel(24, 24).R > baseline.GetPixel(24, 24).R + 20);
                using (var uncorrected = PhotoEngine.RenderImage(photo, false))
                    check("Main preview omits RAW development adjustments", uncorrected.GetPixel(24, 24).ToArgb() == baseline.GetPixel(24, 24).ToArgb());
                photo.Adjustments.RawExposureSteps = 0;
                photo.Adjustments.RawWhiteBalance = "Incandescent";
                using (var balanced = PhotoEngine.RenderImage(photo, true))
                    check("RAW white balance changes developed colors", balanced.GetPixel(24, 24).ToArgb() != baseline.GetPixel(24, 24).ToArgb());
                photo.Adjustments.Monochrome = true;
                using (var monochrome = PhotoEngine.RenderImage(photo, true))
                {
                    Color pixel = monochrome.GetPixel(24, 24);
                    check("RAW monochrome produces equal channels", pixel.R == pixel.G && pixel.G == pixel.B);
                }
            }
            check("RAW decoding leaves original bytes untouched", Digest(raw) == digest);
            using (var preview = RawDecoder.LoadPreview(raw))
                check("RAW browser uses smaller half-size development", preview.Width == 64 && preview.Height == 48);
            photo.Rotation = 1;
            using (var thumbnail = PhotoEngine.CreateThumbnail(photo, 100, 100))
                check("RAW thumbnail remains oriented and transparent outside fitted image", thumbnail.GetPixel(50, 50).A == 255 && thumbnail.GetPixel(1, 50).A == 0);
            photo.Rotation = 0;
            string rotated = Path.Combine(directory, "generated-RAW-rotated.dng");
            WriteDng(rotated, 6);
            var oriented = new PhotoItem(rotated);
            using (var image = PhotoEngine.RenderImage(oriented, false))
                check("RAW camera orientation matches metadata and bitmap", oriented.Width == 96 && oriented.Height == 128 && image.Width == 96 && image.Height == 128);
            using (var histogram = PhotoEngine.CreateHistogram(photo, 256, 100))
            {
                int changed = 0;
                for (int y = 0; y < histogram.Height; y++) for (int x = 0; x < histogram.Width; x++)
                    if (histogram.GetPixel(x, y).ToArgb() != Color.FromArgb(24, 26, 30).ToArgb()) changed++;
                check("RAW histogram contains generated RGB curves", changed > 300);
            }
            string invalid = Path.Combine(directory, "corrupt-RAW.dng");
            File.WriteAllBytes(invalid, new byte[64]);
            bool rejected = false;
            try { new PhotoItem(invalid); } catch (InvalidDataException) { rejected = true; }
            check("Corrupt RAW is rejected without a decoder crash in app", rejected);
            check("Decoder extensions recognize mixed case and reject arbitrary files", PhotoEngine.CanDecode("Photo.CR3") && PhotoEngine.CanDecode("Photo.NEF") && !PhotoEngine.CanDecode("Photo.exe"));
            check("Recovered luminance coefficients used without array allocation", FixedPointColorMath.Luminance(255, 0, 0) == 76 && FixedPointColorMath.Luminance(0, 255, 0) == 149 && FixedPointColorMath.Luminance(255, 255, 255) == 254);
            VerifyProfile(directory, check);
        }

        private sealed class Tag
        {
            public ushort Id, Type;
            public byte[] Data;
            public Tag(ushort id, ushort type, byte[] data) { Id = id; Type = type; Data = data; }
        }
        private static byte[] Shorts(params ushort[] values) { using (var memory = new MemoryStream()) { using (var writer = new BinaryWriter(memory)) foreach (ushort value in values) writer.Write(value); return memory.ToArray(); } }
        private static byte[] Longs(params uint[] values) { using (var memory = new MemoryStream()) { using (var writer = new BinaryWriter(memory)) foreach (uint value in values) writer.Write(value); return memory.ToArray(); } }
        private static byte[] Rationals(params double[] values) { using (var memory = new MemoryStream()) { using (var writer = new BinaryWriter(memory)) foreach (double value in values) { writer.Write((int)Math.Round(value * 10000)); writer.Write(10000); } return memory.ToArray(); } }
        private static byte[] Ascii(string value) { return Encoding.ASCII.GetBytes(value + "\0"); }

        internal static void WriteDng(string path, ushort orientation)
        {
            const int width = 128, height = 96;
            var tags = new List<Tag> {
                new Tag(254,4,Longs(0)), new Tag(256,4,Longs(width)), new Tag(257,4,Longs(height)),
                new Tag(258,3,Shorts(16)), new Tag(259,3,Shorts(1)), new Tag(262,3,Shorts(32803)),
                new Tag(271,2,Ascii("FrameSpool")), new Tag(272,2,Ascii("Synthetic Bayer")),
                new Tag(273,4,Longs(0)), new Tag(274,3,Shorts(orientation)), new Tag(277,3,Shorts(1)),
                new Tag(278,4,Longs(height)), new Tag(279,4,Longs(width*height*2)), new Tag(284,3,Shorts(1)),
                new Tag(306,2,Ascii("2016:07:31 17:30:09")), new Tag(33421,3,Shorts(2,2)),
                new Tag(33422,1,new byte[]{0,1,1,2}), new Tag(50706,1,new byte[]{1,4,0,0}),
                new Tag(50707,1,new byte[]{1,1,0,0}), new Tag(50708,2,Ascii("FrameSpool Synthetic Camera")),
                new Tag(50710,1,new byte[]{0,1,2}), new Tag(50711,3,Shorts(1)), new Tag(50714,5,Rationals(0)),
                new Tag(50717,4,Longs(4095)), new Tag(50721,10,Rationals(3.2406,-1.5372,-.4986,-.9689,1.8758,.0415,.0557,-.2040,1.057)),
                new Tag(50728,5,Rationals(1,1,1)), new Tag(50778,3,Shorts(21))
            };
            tags.Sort(delegate(Tag a, Tag b) { return a.Id.CompareTo(b.Id); });
            int extraStart = 8 + 2 + tags.Count * 12 + 4;
            var offsets = new Dictionary<ushort, uint>();
            byte[] extra;
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            {
                foreach (var tag in tags) if (tag.Data.Length > 4)
                {
                    offsets[tag.Id] = (uint)(extraStart + memory.Position);
                    writer.Write(tag.Data);
                    while (memory.Position % 4 != 0) writer.Write((byte)0);
                }
                extra = memory.ToArray();
            }
            using (var output = File.Create(path))
            using (var writer = new BinaryWriter(output))
            {
                writer.Write((byte)'I'); writer.Write((byte)'I'); writer.Write((ushort)42); writer.Write((uint)8); writer.Write((ushort)tags.Count);
                foreach (var tag in tags)
                {
                    writer.Write(tag.Id); writer.Write(tag.Type);
                    int unit = tag.Type == 3 ? 2 : tag.Type == 4 ? 4 : tag.Type == 5 || tag.Type == 10 ? 8 : 1;
                    writer.Write((uint)(tag.Data.Length / unit));
                    if (tag.Id == 273) writer.Write((uint)(extraStart + extra.Length));
                    else if (tag.Data.Length > 4) writer.Write(offsets[tag.Id]);
                    else { writer.Write(tag.Data); for (int i = tag.Data.Length; i < 4; i++) writer.Write((byte)0); }
                }
                writer.Write((uint)0); writer.Write(extra);
                ushort[][] quadrants = { new ushort[]{1600,200,100}, new ushort[]{100,1600,200}, new ushort[]{100,200,1600}, new ushort[]{1000,1000,1000} };
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    int channel = y % 2 == 0 ? x % 2 == 0 ? 0 : 1 : x % 2 == 0 ? 1 : 2;
                    writer.Write(quadrants[(y >= height / 2 ? 2 : 0) + (x >= width / 2 ? 1 : 0)][channel]);
                }
            }
        }

        private static void VerifyProfile(string directory, Action<string, bool> check)
        {
            string plain = Path.Combine(directory, "generated-color.jpg"), profiled = Path.Combine(directory, "generated-profiled.jpg");
            using (var bitmap = new Bitmap(120, 80))
            using (var graphics = Graphics.FromImage(bitmap)) { graphics.Clear(Color.FromArgb(235, 25, 12)); bitmap.Save(plain, ImageFormat.Jpeg); }
            byte[] image = File.ReadAllBytes(plain), profile = CreateSwappedProfile();
            using (var file = File.Create(profiled))
            {
                file.Write(image, 0, 2);
                file.WriteByte(255); file.WriteByte(226);
                int length = profile.Length + 16;
                file.WriteByte((byte)(length >> 8)); file.WriteByte((byte)length);
                byte[] signature = Encoding.ASCII.GetBytes("ICC_PROFILE\0"); file.Write(signature, 0, signature.Length);
                file.WriteByte(1); file.WriteByte(1); file.Write(profile, 0, profile.Length);
                file.Write(image, 2, image.Length - 2);
            }
            var photo = new PhotoItem(profiled);
            check("Embedded ICC profile detected in image metadata", photo.Metadata["Color Management"].StartsWith("Embedded ICC", StringComparison.Ordinal));
            using (var managed = PhotoEngine.LoadImage(profiled))
            using (var unmanaged = PhotoEngine.LoadImage(plain))
                check("Windows embedded ICC transform applied before correction", managed.GetPixel(60, 40).B > managed.GetPixel(60, 40).R && unmanaged.GetPixel(60, 40).R > unmanaged.GetPixel(60, 40).B);
            using (var transparent = new Bitmap(30, 20, PixelFormat.Format32bppArgb))
            {
                transparent.SetPixel(10, 10, Color.FromArgb(91, 230, 25, 12));
                WindowsColorManagement.ConvertToSrgb(transparent, profile);
                Color color = transparent.GetPixel(10, 10);
                check("ICC conversion preserves image alpha", color.A == 91 && color.B > color.R);
            }
            bool rejected = false;
            using (var bitmap = new Bitmap(30, 20))
                try { WindowsColorManagement.ConvertToSrgb(bitmap, new byte[128]); } catch (InvalidDataException) { rejected = true; }
            check("Malformed embedded ICC profiles fail explicitly", rejected);
        }

        // A generated RGB matrix ICC v2 profile, with red and blue primaries swapped.
        // No proprietary profile or camera file is distributed in these tests.
        private static byte[] CreateSwappedProfile()
        {
            var tags = new List<KeyValuePair<string, byte[]>>();
            byte[] description = new byte[128]; PutAscii(description, 0, "desc"); Put32(description, 8, 29); PutAscii(description, 12, "FrameSpool generated profile\0");
            tags.Add(new KeyValuePair<string, byte[]>("desc", description));
            byte[] copyright = new byte[64]; PutAscii(copyright, 0, "text"); PutAscii(copyright, 8, "FrameSpool test fixture, public domain\0");
            tags.Add(new KeyValuePair<string, byte[]>("cprt", copyright));
            tags.Add(new KeyValuePair<string, byte[]>("wtpt", Xyz(.9642, 1, .8249)));
            tags.Add(new KeyValuePair<string, byte[]>("rXYZ", Xyz(.1430804, .0606169, .7141733)));
            tags.Add(new KeyValuePair<string, byte[]>("gXYZ", Xyz(.3850649, .7168786, .0971045)));
            tags.Add(new KeyValuePair<string, byte[]>("bXYZ", Xyz(.4360747, .2225045, .0139322)));
            foreach (string name in new[]{"rTRC","gTRC","bTRC"})
            {
                byte[] curve = new byte[16]; PutAscii(curve, 0, "curv"); Put32(curve, 8, 1); curve[12] = 2; curve[13] = 51;
                tags.Add(new KeyValuePair<string, byte[]>(name, curve));
            }
            int position = 128 + 4 + tags.Count * 12;
            int total = position;
            foreach (var tag in tags) total += (tag.Value.Length + 3) & ~3;
            byte[] profile = new byte[total]; Put32(profile, 0, total); Put32(profile, 8, 0x02100000);
            PutAscii(profile, 12, "mntr"); PutAscii(profile, 16, "RGB "); PutAscii(profile, 20, "XYZ ");
            profile[24] = 7; profile[25] = 234; profile[27] = 10; profile[29] = 11;
            PutAscii(profile, 36, "acsp"); PutAscii(profile, 40, "MSFT");
            Put32(profile, 68, 0x0000f6d6); Put32(profile, 72, 0x00010000); Put32(profile, 76, 0x0000d32d);
            Put32(profile, 128, tags.Count);
            for (int i = 0; i < tags.Count; i++)
            {
                PutAscii(profile, 132 + i * 12, tags[i].Key); Put32(profile, 136 + i * 12, position); Put32(profile, 140 + i * 12, tags[i].Value.Length);
                Array.Copy(tags[i].Value, 0, profile, position, tags[i].Value.Length); position += (tags[i].Value.Length + 3) & ~3;
            }
            return profile;
        }
        private static byte[] Xyz(double x, double y, double z) { byte[] bytes = new byte[20]; PutAscii(bytes, 0, "XYZ "); Put32(bytes, 8, (int)Math.Round(x * 65536)); Put32(bytes, 12, (int)Math.Round(y * 65536)); Put32(bytes, 16, (int)Math.Round(z * 65536)); return bytes; }
        private static void PutAscii(byte[] bytes, int offset, string value) { Array.Copy(Encoding.ASCII.GetBytes(value), 0, bytes, offset, value.Length); }
        private static void Put32(byte[] bytes, int offset, int value) { bytes[offset]=(byte)(value>>24); bytes[offset+1]=(byte)(value>>16); bytes[offset+2]=(byte)(value>>8); bytes[offset+3]=(byte)value; }
        private static string Digest(string path) { using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(file)); }
    }
}
