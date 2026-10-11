using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FrameSpool
{
    // All adjustments are independent state. No operation modifies the source file.
    public sealed class PhotoAdjustments
    {
        public string Mode = "PhotoEnhance";
        public string Scene = "Standard";
        public int SceneLevel;
        public int Brightness;
        public bool Vivid;
        public bool Monochrome;
        public bool DigitalCorrection;
        public bool SmoothSkin;
        public bool AutoRedEye;
        // Coordinates are normalized to the EXIF-oriented original, before user rotation.
        public List<PointF> ManualEyePoints = new List<PointF>();
        public int Slimming;
        public int FaceDefinition;
        public int SkinRadiance;

        public PhotoAdjustments Clone()
        {
            PhotoAdjustments result = (PhotoAdjustments)MemberwiseClone();
            result.ManualEyePoints = new List<PointF>(ManualEyePoints ?? new List<PointF>());
            return result;
        }

        public void Reset()
        {
            Mode = "PhotoEnhance";
            Scene = "Standard";
            SceneLevel = Brightness = Slimming = FaceDefinition = SkinRadiance = 0;
            Vivid = Monochrome = DigitalCorrection = SmoothSkin = AutoRedEye = false;
            if (ManualEyePoints == null) ManualEyePoints = new List<PointF>();
            else ManualEyePoints.Clear();
        }

        public bool HasCorrections
        {
            get
            {
                return !String.Equals(Mode, "None", StringComparison.OrdinalIgnoreCase) ||
                    Brightness != 0 || Vivid || Monochrome || DigitalCorrection || AutoRedEye ||
                    (ManualEyePoints != null && ManualEyePoints.Count != 0) ||
                    Slimming != 0 || FaceDefinition != 0 || SkinRadiance != 0;
            }
        }
    }

    public sealed class PhotoItem
    {
        private int rotation;
        private float zoom = 1.0f;
        private int copies;
        private string comment = String.Empty;
        private PhotoAdjustments adjustments;
        public PhotoItem Original { get; private set; }

        public string FilePath { get; private set; }
        public string FileName { get { return Path.GetFileName(FilePath); } }
        public DateTime CaptureDate { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public long FileSize { get; private set; }
        public Dictionary<string, string> Metadata { get; private set; }
        public int Rotation
        {
            get { return rotation; }
            set { rotation = ((value % 4) + 4) % 4; }
        }
        public float Zoom
        {
            get { return zoom; }
            set { zoom = Single.IsNaN(value) || Single.IsInfinity(value) ? 1f : Math.Max(.5f, Math.Min(2f, value)); }
        }
        public float OffsetX { get; set; }
        public float OffsetY { get; set; }
        // false = Trim (cover); true = Fit (contain).
        public bool Fit { get; set; }
        public int Copies
        {
            get { return copies; }
            set { copies = Math.Max(0, Math.Min(99, value)); }
        }
        public string Comment
        {
            get { return Original==null?comment:Original.Comment; }
            set
            {
                string text = value ?? String.Empty;
                if (text.Length > 60)
                {
                    int length = 60;
                    // Do not cut a UTF-16 surrogate pair in half.
                    if (Char.IsHighSurrogate(text[length - 1])) length--;
                    text = text.Substring(0, length);
                }
                if(Original!=null)Original.Comment=text;else comment = text;
            }
        }
        public PhotoAdjustments Adjustments { get {return Original==null?adjustments:Original.Adjustments;} set {if(Original==null)adjustments=value;else Original.Adjustments=value;} }

        public PhotoItem(string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A photo file path is required.", "filePath");
            FilePath = Path.GetFullPath(filePath);
            FileInfo info = new FileInfo(FilePath);
            if (!info.Exists) throw new FileNotFoundException("Cannot find the photo.", FilePath);
            FileSize = info.Length;
            CaptureDate = info.LastWriteTime;
            Metadata = new Dictionary<string, string>();
            Adjustments = new PhotoAdjustments();
            using (FileStream stream = File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image image = Image.FromStream(stream, true, true))
            {
                int orientation = PhotoEngine.GetExifOrientation(image);
                Width = orientation >= 5 && orientation <= 8 ? image.Height : image.Width;
                Height = orientation >= 5 && orientation <= 8 ? image.Width : image.Height;
                CaptureDate = PhotoEngine.GetCaptureDate(image, CaptureDate);
                PhotoEngine.ReadMetadata(image, Metadata);
            }
            string validation = PhotoEngine.ValidateDimensions(Width, Height, 60);
            if (validation != null) throw new ArgumentException(validation, "filePath");
        }

        private PhotoItem() { }

        public PhotoItem Clone()
        {
            PhotoItem result = new PhotoItem();
            result.FilePath = FilePath;
            result.CaptureDate = CaptureDate;
            result.Width = Width;
            result.Height = Height;
            result.FileSize = FileSize;
            result.Metadata = new Dictionary<string, string>(Metadata);
            result.Rotation = Rotation;
            result.Zoom = Zoom;
            result.OffsetX = OffsetX;
            result.OffsetY = OffsetY;
            result.Fit = Fit;
            result.Copies = Copies;
            result.Comment = Comment;
            result.Adjustments = (Adjustments ?? new PhotoAdjustments()).Clone();
            return result;
        }
        public PhotoItem CreatePlacement()
        {var result=Clone();result.Original=Original??this;return result;}

        public void ResetPosition()
        {
            Rotation = 0;
            Zoom = 1f;
            OffsetX = OffsetY = 0f;
            Fit = false;
        }

        public void ResetCorrection()
        {
            Adjustments = new PhotoAdjustments();
        }
    }

    public static class PhotoEngine
    {
        public const int MaximumManualEyes = 20;
        public const long MaximumRedEyePixels = 20000000;
        public const string CorrectionFidelityNote =
            "PhotoEnhance, scene, red-eye, skin and face adjustments use independent approximate algorithms. " +
            "PRINT Image Matching metadata and proprietary RAW development are not interpreted.";

        public static string ValidateDimensions(int width, int height, int minimum = 60)
        {
            minimum = Math.Max(60, Math.Min(600, minimum));
            if (width <= 0 || height <= 0) return "The photo has invalid dimensions.";
            if (Math.Min(width, height) < minimum) return "The photo's short side must be at least " + minimum + " pixels.";
            if ((double)Math.Max(width, height) / Math.Min(width, height) > 15.0)
                return "The photo's aspect ratio cannot exceed 15:1.";
            return null;
        }

        public static bool CanCorrectRedEye(PhotoItem photo)
        {
            return photo != null && (long)photo.Width * photo.Height <= MaximumRedEyePixels &&
                photo.Width <= 32767 && photo.Height <= 32767;
        }

        public static Bitmap LoadImage(string filePath)
        {
            using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image source = Image.FromStream(stream, true, true))
            {
                ApplyExifOrientation(source, GetExifOrientation(source));
                Bitmap bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                try
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    }
                    return bitmap;
                }
                catch { bitmap.Dispose(); throw; }
            }
        }

        public static Bitmap CreateThumbnail(PhotoItem photo, int width, int height)
        {
            if (photo == null) throw new ArgumentNullException("photo");
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException("width");
            using (Bitmap source = RenderImage(photo, false))
            {
                Bitmap thumbnail = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                try
                {
                    using (Graphics graphics = Graphics.FromImage(thumbnail))
                    {
                        SetQuality(graphics);
                        graphics.Clear(Color.Transparent);
                        float scale = Math.Min((float)width / source.Width, (float)height / source.Height);
                        float w = source.Width * scale, h = source.Height * scale;
                        DrawBitmap(graphics, source, new RectangleF((width - w) / 2f, (height - h) / 2f, w, h));
                    }
                    return thumbnail;
                }
                catch { thumbnail.Dispose(); throw; }
            }
        }

        // The caller owns the returned Bitmap and must dispose it.
        public static Bitmap RenderImage(PhotoItem photo, bool corrected)
        {
            if (photo == null) throw new ArgumentNullException("photo");
            Bitmap image = LoadImage(photo.FilePath);
            try
            {
                PhotoAdjustments adjustments = photo.Adjustments ?? new PhotoAdjustments();
                if (corrected && adjustments.HasCorrections)
                {
                    if (CanCorrectRedEye(photo) && (adjustments.AutoRedEye ||
                        (adjustments.ManualEyePoints != null && adjustments.ManualEyePoints.Count > 0)))
                        CorrectRedEye(image, adjustments);
                    ApplyTone(image, adjustments);
                    if (adjustments.DigitalCorrection) SmoothNoise(image,adjustments.SmoothSkin);
                    if (Math.Min(image.Width, image.Height) >= 240 &&
                        (adjustments.FaceDefinition > 0 || adjustments.SkinRadiance > 0))
                        ApplyPortraitTone(image, adjustments);
                }
                if (photo.Rotation != 0) image.RotateFlip((RotateFlipType)photo.Rotation);
                if (corrected && adjustments.Slimming > 0)
                {
                    Bitmap slimmed = ApplySlimming(image, Math.Max(0, Math.Min(3, adjustments.Slimming)));
                    image.Dispose();
                    image = slimmed;
                }
                return image;
            }
            catch { image.Dispose(); throw; }
        }

        public static void DrawPhoto(Graphics graphics, PhotoItem photo, RectangleF bounds, bool corrected)
        { DrawPhoto(graphics,photo,bounds,corrected,0,1,1); }
        public static void DrawPhoto(Graphics graphics, PhotoItem photo, RectangleF bounds, bool corrected,int layoutRotation,float offsetScaleX,float offsetScaleY)
        {
            if (graphics == null) throw new ArgumentNullException("graphics");
            if (photo == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            using (Bitmap image = RenderImage(photo, corrected))
            {
                switch(((layoutRotation%4)+4)%4){case 1:image.RotateFlip(RotateFlipType.Rotate90FlipNone);break;case 2:image.RotateFlip(RotateFlipType.Rotate180FlipNone);break;case 3:image.RotateFlip(RotateFlipType.Rotate270FlipNone);break;}
                float scaleX = bounds.Width / image.Width;
                float scaleY = bounds.Height / image.Height;
                float scale = (photo.Fit ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY)) * photo.Zoom;
                float width = image.Width * scale, height = image.Height * scale;
                float x = bounds.X + (bounds.Width - width) / 2f + Finite(photo.OffsetX)*offsetScaleX;
                float y = bounds.Y + (bounds.Height - height) / 2f + Finite(photo.OffsetY)*offsetScaleY;
                GraphicsState state = graphics.Save();
                try
                {
                    graphics.SetClip(bounds, CombineMode.Intersect);
                    SetQuality(graphics);
                    DrawBitmap(graphics, image, new RectangleF(x, y, width, height));
                }
                finally { graphics.Restore(state); }
            }
        }

        // Convert a point in the rotated image display back to original normalized coordinates.
        public static PointF ToOriginalEyePoint(PointF normalizedPoint, int rotation)
        {
            float x = Math.Max(0f, Math.Min(1f, normalizedPoint.X));
            float y = Math.Max(0f, Math.Min(1f, normalizedPoint.Y));
            switch (((rotation % 4) + 4) % 4)
            {
                case 1: return new PointF(y, 1f - x);
                case 2: return new PointF(1f - x, 1f - y);
                case 3: return new PointF(1f - y, x);
                default: return new PointF(x, y);
            }
        }

        public static bool AddManualEye(PhotoItem photo, PointF originalNormalizedPoint)
        {
            if (photo == null || !CanCorrectRedEye(photo)) return false;
            if (photo.Adjustments == null) photo.Adjustments = new PhotoAdjustments();
            if (photo.Adjustments.ManualEyePoints == null) photo.Adjustments.ManualEyePoints = new List<PointF>();
            if (photo.Adjustments.ManualEyePoints.Count >= MaximumManualEyes) return false;
            photo.Adjustments.ManualEyePoints.Add(new PointF(
                Math.Max(0f, Math.Min(1f, originalNormalizedPoint.X)),
                Math.Max(0f, Math.Min(1f, originalNormalizedPoint.Y))));
            return true;
        }

        internal static int GetExifOrientation(Image image)
        {
            try
            {
                byte[] value = image.GetPropertyItem(0x0112).Value;
                return value.Length >= 2 ? BitConverter.ToUInt16(value, 0) : 1;
            }
            catch (ArgumentException) { return 1; }
        }

        internal static DateTime GetCaptureDate(Image image, DateTime fallback)
        {
            foreach (int property in new int[] { 0x9003, 0x9004, 0x0132 })
            {
                string value = GetAsciiProperty(image, property);
                DateTime parsed;
                if (DateTime.TryParseExact(value, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsed)) return parsed;
            }
            return fallback;
        }

        internal static void ReadMetadata(Image image, Dictionary<string, string> output)
        {
            output["File Type"] = image.RawFormat.Equals(ImageFormat.Jpeg) ? "JPEG" :
                image.RawFormat.Equals(ImageFormat.Tiff) ? "TIFF" :
                image.RawFormat.Equals(ImageFormat.Bmp) ? "BMP" :
                image.RawFormat.Equals(ImageFormat.Png) ? "PNG" : image.RawFormat.ToString();
            output["Camera"] = (GetAsciiProperty(image, 0x010F) + " " + GetAsciiProperty(image, 0x0110)).Trim();
            output["EXP Time"] = GetRationalProperty(image, 0x829A, true);
            output["F Number"] = GetRationalProperty(image, 0x829D, false);
            output["ISO Sensitivity"] = GetUnsignedProperty(image, 0x8827);
            string flash = GetUnsignedProperty(image, 0x9209);
            int flashValue;
            output["Flash"] = Int32.TryParse(flash, out flashValue) ? ((flashValue & 1) != 0 ? "ON" : "OFF") : "-";
            string colorSpace = GetUnsignedProperty(image, 0xA001);
            output["Exif/DCF Color Space"] = colorSpace == "1" ? "Standard (sRGB)" : colorSpace == "65535" ? "DCF Option" : "-";
            output["Mono. Info"] = "-";
        }

        private static string GetAsciiProperty(Image image, int id)
        {
            try { return Encoding.ASCII.GetString(image.GetPropertyItem(id).Value).TrimEnd('\0').Trim(); }
            catch (ArgumentException) { return String.Empty; }
        }

        private static string GetUnsignedProperty(Image image, int id)
        {
            try
            {
                PropertyItem property = image.GetPropertyItem(id);
                if (property.Type == 3 && property.Value.Length >= 2)
                    return BitConverter.ToUInt16(property.Value, 0).ToString(CultureInfo.InvariantCulture);
                if (property.Type == 4 && property.Value.Length >= 4)
                    return BitConverter.ToUInt32(property.Value, 0).ToString(CultureInfo.InvariantCulture);
            }
            catch (ArgumentException) { }
            return "-";
        }

        private static string GetRationalProperty(Image image, int id, bool exposure)
        {
            try
            {
                byte[] value = image.GetPropertyItem(id).Value;
                if (value.Length < 8) return "-";
                uint numerator = BitConverter.ToUInt32(value, 0), denominator = BitConverter.ToUInt32(value, 4);
                if (denominator == 0) return "-";
                double result = (double)numerator / denominator;
                if (exposure && result > 0 && result < 1)
                    return "1/" + Math.Round(1 / result).ToString(CultureInfo.InvariantCulture) + " s";
                return result.ToString("0.###", CultureInfo.InvariantCulture) + (exposure ? " s" : String.Empty);
            }
            catch (ArgumentException) { return "-"; }
        }

        private static void ApplyExifOrientation(Image image, int orientation)
        {
            switch (orientation)
            {
                case 2: image.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
                case 3: image.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                case 4: image.RotateFlip(RotateFlipType.Rotate180FlipX); break;
                case 5: image.RotateFlip(RotateFlipType.Rotate90FlipX); break;
                case 6: image.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                case 7: image.RotateFlip(RotateFlipType.Rotate270FlipX); break;
                case 8: image.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
            }
        }

        private static void SetQuality(Graphics graphics)
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
        }

        private static void DrawBitmap(Graphics graphics, Bitmap image, RectangleF bounds)
        {
            using (ImageAttributes attributes = new ImageAttributes())
            {
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                // This overload preserves subpixel placement while preventing dark edge sampling.
                PointF[] destination = new PointF[] {
                    new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Top),
                    new PointF(bounds.Left, bounds.Bottom) };
                graphics.DrawImage(image, destination, new RectangleF(0, 0, image.Width, image.Height),
                    GraphicsUnit.Pixel, attributes);
            }
        }

        private static float Finite(float value)
        {
            return Single.IsNaN(value) || Single.IsInfinity(value) ? 0f : value;
        }

        private static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
        private static byte Channel(double value) { return (byte)Math.Max(0, Math.Min(255, Math.Round(value))); }

        private static byte[] ReadPixels(Bitmap image)
        {
            Rectangle rectangle = new Rectangle(0, 0, image.Width, image.Height);
            BitmapData bits = image.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = image.Width * 4;
                byte[] pixels = new byte[checked(rowBytes * image.Height)];
                for (int y = 0; y < image.Height; y++)
                    Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), pixels, y * rowBytes, rowBytes);
                return pixels;
            }
            finally { image.UnlockBits(bits); }
        }

        private static void WritePixels(Bitmap image, byte[] pixels)
        {
            BitmapData bits = image.LockBits(new Rectangle(0, 0, image.Width, image.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = image.Width * 4;
                for (int y = 0; y < image.Height; y++)
                    Marshal.Copy(pixels, y * rowBytes, IntPtr.Add(bits.Scan0, y * bits.Stride), rowBytes);
            }
            finally { image.UnlockBits(bits); }
        }

        private static void ApplyTone(Bitmap image, PhotoAdjustments adjustments)
        {
            // Independent global tone approximation: no proprietary LUT/profile is used.
            bool enhance = String.Equals(adjustments.Mode, "PhotoEnhance", StringComparison.OrdinalIgnoreCase);
            double contrast = enhance ? 1.06 : 1.0;
            double saturation = enhance ? 1.05 : 1.0;
            double exposure = Math.Pow(2.0, Clamp(adjustments.Brightness, -100, 100) / 100.0);
            double redMultiplier = 1, blueMultiplier = 1;
            if (enhance)
            {
                double sceneStrength = .5 + Clamp(adjustments.SceneLevel, -100, 100) / 200.0;
                if (String.Equals(adjustments.Scene, "People", StringComparison.OrdinalIgnoreCase))
                { redMultiplier += .04 * sceneStrength; blueMultiplier -= .02 * sceneStrength; }
                else if (String.Equals(adjustments.Scene, "Landscape", StringComparison.OrdinalIgnoreCase))
                { saturation += .2 * sceneStrength; contrast += .08 * sceneStrength; }
                else if (String.Equals(adjustments.Scene, "Night", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(adjustments.Scene, "Night Scene", StringComparison.OrdinalIgnoreCase))
                { contrast += .15 * sceneStrength; exposure *= 1.0 + .12 * sceneStrength; }
            }
            if (adjustments.Vivid) { saturation *= 1.25; contrast *= 1.1; }
            if (!enhance && adjustments.Brightness == 0 && !adjustments.Vivid && !adjustments.Monochrome) return;
            byte[] pixels = ReadPixels(image);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                double b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                double gray = .2126 * r + .7152 * g + .0722 * b;
                if (adjustments.Monochrome) r = g = b = gray;
                else
                { r = gray + (r - gray) * saturation; g = gray + (g - gray) * saturation; b = gray + (b - gray) * saturation; }
                pixels[i] = Channel(((b - 127.5) * contrast + 127.5) * exposure * blueMultiplier);
                pixels[i + 1] = Channel(((g - 127.5) * contrast + 127.5) * exposure);
                pixels[i + 2] = Channel(((r - 127.5) * contrast + 127.5) * exposure * redMultiplier);
            }
            WritePixels(image, pixels);
        }

        private static void SmoothNoise(Bitmap image,bool smoothSkin)
        {
            // Small bilateral-like neighborhood: avoids blending across strong luminance edges.
            byte[] source = ReadPixels(image), output = (byte[])source.Clone();
            int w = image.Width, h = image.Height;
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = (y * w + x) * 4;
                    int sumB = source[i] * 4, sumG = source[i + 1] * 4, sumR = source[i + 2] * 4, count = 4;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int j = ((y + dy) * w + x + dx) * 4;
                        if(smoothSkin && !(source[i+2]>source[i+1]&&source[i+1]>source[i]&&source[i+2]-source[i]>15))continue;
                        if (Math.Abs(source[i] - source[j]) + Math.Abs(source[i + 1] - source[j + 1]) +
                            Math.Abs(source[i + 2] - source[j + 2]) > 72) continue;
                        sumB += source[j]; sumG += source[j + 1]; sumR += source[j + 2]; count++;
                    }
                    output[i] = (byte)(sumB / count); output[i + 1] = (byte)(sumG / count); output[i + 2] = (byte)(sumR / count);
                }
            WritePixels(image, output);
        }

        private static bool IsRed(byte[] pixels, int pixel)
        {
            int i = pixel * 4;
            int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            return pixels[i + 3] > 0 && r > 70 && r > g * 1.5 && r > b * 1.5 && r - Math.Max(g, b) > 35;
        }

        private static void DesaturateRed(byte[] pixels, int pixel)
        {
            int i = pixel * 4;
            byte level = (byte)((pixels[i] + pixels[i + 1]) / 2);
            pixels[i + 2] = level;
        }

        private static void CorrectRedEye(Bitmap image, PhotoAdjustments adjustments)
        {
            // Approximation: automatic correction accepts small compact red connected components.
            // It may mistake small red objects for pupils, as the original help also cautions.
            int w = image.Width, h = image.Height;
            byte[] pixels = ReadPixels(image);
            if (adjustments.AutoRedEye)
            {
                byte[] labels = new byte[checked(w * h)];
                for (int p = 0; p < labels.Length; p++) if (IsRed(pixels, p)) labels[p] = 1;
                Queue<int> queue = new Queue<int>();
                List<int> component = new List<int>();
                int maxSide = Math.Max(3, Math.Min(80, Math.Min(w, h) / 30));
                int maxPixels = maxSide * maxSide;
                for (int start = 0; start < labels.Length; start++)
                {
                    if (labels[start] != 1) continue;
                    labels[start] = 2; queue.Enqueue(start); component.Clear();
                    int minX = w, maxX = 0, minY = h, maxY = 0, count = 0;
                    while (queue.Count > 0)
                    {
                        int p = queue.Dequeue(), x = p % w, y = p / w; count++;
                        if (component.Count <= maxPixels) component.Add(p);
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                        if (x > 0) EnqueueRed(labels, p - 1, queue);
                        if (x + 1 < w) EnqueueRed(labels, p + 1, queue);
                        if (y > 0) EnqueueRed(labels, p - w, queue);
                        if (y + 1 < h) EnqueueRed(labels, p + w, queue);
                    }
                    int cw = maxX - minX + 1, ch = maxY - minY + 1;
                    if (count >= 2 && count <= maxPixels && cw <= maxSide && ch <= maxSide &&
                        cw <= ch * 2.5 && ch <= cw * 2.5 && count >= cw * ch * .25)
                        foreach (int p in component) DesaturateRed(pixels, p);
                }
            }
            if (adjustments.ManualEyePoints != null)
            {
                int radius = Math.Max(2, Math.Min(50, (int)(Math.Min(w, h) * .018)));
                for (int n = 0; n < Math.Min(MaximumManualEyes, adjustments.ManualEyePoints.Count); n++)
                {
                    PointF point = adjustments.ManualEyePoints[n];
                    int cx = Clamp((int)(point.X * (w - 1)), 0, w - 1), cy = Clamp((int)(point.Y * (h - 1)), 0, h - 1);
                    for (int y = Math.Max(0, cy - radius); y <= Math.Min(h - 1, cy + radius); y++)
                        for (int x = Math.Max(0, cx - radius); x <= Math.Min(w - 1, cx + radius); x++)
                            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius && IsRed(pixels, y * w + x))
                                DesaturateRed(pixels, y * w + x);
                }
            }
            WritePixels(image, pixels);
        }

        private static void EnqueueRed(byte[] labels, int pixel, Queue<int> queue)
        {
            if (labels[pixel] == 1) { labels[pixel] = 2; queue.Enqueue(pixel); }
        }

        private static void ApplyPortraitTone(Bitmap image, PhotoAdjustments adjustments)
        {
            // Approximate skin-color enhancement, not face recognition or facial reshaping.
            double radiance = adjustments.Monochrome ? 0 : Clamp(adjustments.SkinRadiance, 0, 5) * .025;
            double definition = Clamp(adjustments.FaceDefinition, 0, 5) * .02;
            byte[] pixels = ReadPixels(image);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                double b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                bool skin = r > 70 && g > 35 && b > 20 && r > g && r > b && r - Math.Min(g, b) > 15 && Math.Abs(r - g) > 8;
                if (!skin) continue;
                pixels[i] = Channel(((b - 127.5) * (1 + definition) + 127.5) * (1 + radiance));
                pixels[i + 1] = Channel(((g - 127.5) * (1 + definition) + 127.5) * (1 + radiance));
                pixels[i + 2] = Channel(((r - 127.5) * (1 + definition) + 127.5) * (1 + radiance));
            }
            WritePixels(image, pixels);
        }

        private static Bitmap ApplySlimming(Bitmap image, int level)
        {
            // Independent symmetric horizontal warp, intentionally documented as approximate.
            int w = image.Width, h = image.Height;
            byte[] source = ReadPixels(image), target = new byte[source.Length];
            double strength = level * .035, center = (w - 1) / 2.0;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double normalized = center == 0 ? 0 : (x - center) / center;
                double srcX = center + (x - center) * (1 + strength * (1 - normalized * normalized));
                int left = Clamp((int)Math.Floor(srcX), 0, w - 1), right = Math.Min(w - 1, left + 1);
                double fraction = Math.Max(0, Math.Min(1, srcX - Math.Floor(srcX)));
                int from = (y * w + left) * 4, next = (y * w + right) * 4, to = (y * w + x) * 4;
                for (int c = 0; c < 4; c++) target[to + c] = Channel(source[from + c] * (1 - fraction) + source[next + c] * fraction);
            }
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            try { WritePixels(result, target); return result; }
            catch { result.Dispose(); throw; }
        }
    }
}
