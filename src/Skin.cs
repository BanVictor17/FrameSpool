using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FrameSpool
{
    /// <summary>Loads the original declarative UI artwork as data; no code is loaded.</summary>
    public static class Skin
    {
        private static readonly Dictionary<string, Image> Images =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Sync = new object();

        private static string Resolve(string relativeName)
        {
            if (String.IsNullOrEmpty(relativeName)) return null;
            string name = relativeName.Replace('/', Path.DirectorySeparatorChar);
            if (String.Equals(Path.GetExtension(name), ".tga", StringComparison.OrdinalIgnoreCase))
                name = Path.ChangeExtension(name, ".png");
            return AppAssets.Contains(name) ? name : null;
        }

        public static Image GetImage(string relativeName)
        {
            string path = Resolve(relativeName);
            if (path == null) return null;
            lock (Sync)
            {
                Image existing;
                if (Images.TryGetValue(path, out existing)) return existing;
                using (Stream sourceStream = AppAssets.Open(path))
                using (Image source = Image.FromStream(sourceStream))
                {
                    Bitmap bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        // Shipped BMPs declare 71.12 DPI. Explicit pixel source/destination
                        // rectangles preserve their real dimensions on a 96-DPI bitmap.
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                        graphics.PixelOffsetMode = PixelOffsetMode.Half;
                        graphics.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                            0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
                    }
                    string extension = Path.GetExtension(path);
                    if (String.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase)
                        && HasButtonSuffix(path))
                    {
                        string maskPath = Path.Combine(Path.GetDirectoryName(path),
                            Path.GetFileNameWithoutExtension(path).Substring(0,
                                Path.GetFileNameWithoutExtension(path).Length - 1) + "m.bmp");
                        if (AppAssets.Contains(maskPath))
                        {
                            using (Stream maskStream = AppAssets.Open(maskPath))
                            using (Bitmap mask = new Bitmap(maskStream)) ApplyMask(bitmap, mask);
                        }
                        else RemoveCornerBackground(bitmap);
                    }
                    Images[path] = bitmap;
                    return bitmap;
                }
            }
        }

        private static bool HasButtonSuffix(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Length < 3 || name[name.Length - 2] != '_') return false;
            return "udfx".IndexOf(Char.ToLowerInvariant(name[name.Length - 1])) >= 0;
        }

        private static void ApplyMask(Bitmap image, Bitmap mask)
        {
            if (mask.Width != image.Width || mask.Height != image.Height) return;
            Rectangle bounds = new Rectangle(0, 0, image.Width, image.Height);
            BitmapData data = image.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                byte[] bytes = new byte[Math.Abs(data.Stride) * data.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++)
                        bytes[y * data.Stride + x * 4 + 3] = mask.GetPixel(x, y).R;
                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            }
            finally { image.UnlockBits(data); }
        }

        private static void RemoveCornerBackground(Bitmap image)
        {
            // Only erase corner-connected pixels, preserving white icon details.
            Color background = image.GetPixel(0, 0);
            int wanted = background.ToArgb();
            bool[] visited = new bool[image.Width * image.Height];
            Queue<Point> queue = new Queue<Point>();
            queue.Enqueue(new Point(0, 0));
            queue.Enqueue(new Point(image.Width - 1, 0));
            queue.Enqueue(new Point(0, image.Height - 1));
            queue.Enqueue(new Point(image.Width - 1, image.Height - 1));
            while (queue.Count > 0)
            {
                Point point = queue.Dequeue();
                if (point.X < 0 || point.Y < 0 || point.X >= image.Width || point.Y >= image.Height) continue;
                int index = point.Y * image.Width + point.X;
                if (visited[index]) continue;
                visited[index] = true;
                if (image.GetPixel(point.X, point.Y).ToArgb() != wanted) continue;
                image.SetPixel(point.X, point.Y, Color.Transparent);
                queue.Enqueue(new Point(point.X - 1, point.Y));
                queue.Enqueue(new Point(point.X + 1, point.Y));
                queue.Enqueue(new Point(point.X, point.Y - 1));
                queue.Enqueue(new Point(point.X, point.Y + 1));
            }
        }
    }

    /// <summary>Accessible selectable control drawing the recovered bitmap button states.</summary>
    public class SkinButton : Control
    {
        private string imagePath;
        private Rectangle captionRectangle;
        private ContentAlignment captionAlignment = ContentAlignment.MiddleCenter;
        private float textPixelSize = 12;
        private bool textBold;
        private bool mouseOver;
        private bool mousePressed;
        private bool keyboardPressed;

        public SkinButton(string text, string imagePath)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            this.imagePath = imagePath;
            Text = text;
            BackColor = Color.Transparent;
            ForeColor = Color.Black;
            TabStop = true;
            Cursor = Cursors.Default;
            AccessibleRole = AccessibleRole.PushButton;
            Image image = Skin.GetImage(imagePath);
            Size = image == null ? new Size(100, 30) : image.Size;
        }

        public string ImagePath { get { return imagePath; } set { imagePath = value; Invalidate(); } }
        public Rectangle CaptionRectangle { get { return captionRectangle; } set { captionRectangle = value; Invalidate(); } }
        public ContentAlignment CaptionAlignment { get { return captionAlignment; } set { captionAlignment = value; Invalidate(); } }
        public float TextPixelSize { get { return textPixelSize; } set { textPixelSize = Math.Max(1, value); Invalidate(); } }
        public bool TextBold { get { return textBold; } set { textBold = value; Invalidate(); } }

        private string StatePath(char state)
        {
            if (String.IsNullOrEmpty(imagePath)) return imagePath;
            string extension = Path.GetExtension(imagePath);
            string stem = imagePath.Substring(0, imagePath.Length - extension.Length);
            if (stem.Length >= 2 && stem[stem.Length - 2] == '_'
                && "udfxm".IndexOf(Char.ToLowerInvariant(stem[stem.Length - 1])) >= 0)
                return stem.Substring(0, stem.Length - 1) + state + extension;
            return imagePath;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            char state = !Enabled ? 'x' : (mousePressed || keyboardPressed) ? 'd' : (mouseOver || Focused) ? 'f' : 'u';
            Image image = Skin.GetImage(StatePath(state)) ?? Skin.GetImage(imagePath);
            if (image != null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                e.Graphics.DrawImage(image, ClientRectangle, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel);
            }
            else ButtonRenderer.DrawButton(e.Graphics, ClientRectangle,
                !Enabled ? System.Windows.Forms.VisualStyles.PushButtonState.Disabled :
                mousePressed || keyboardPressed ? System.Windows.Forms.VisualStyles.PushButtonState.Pressed :
                mouseOver ? System.Windows.Forms.VisualStyles.PushButtonState.Hot :
                System.Windows.Forms.VisualStyles.PushButtonState.Normal);

            Rectangle rectangle = captionRectangle.IsEmpty ? ClientRectangle : captionRectangle;
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.PreserveGraphicsClipping;
            if (captionAlignment == ContentAlignment.TopCenter || captionAlignment == ContentAlignment.MiddleCenter || captionAlignment == ContentAlignment.BottomCenter)
                flags |= TextFormatFlags.HorizontalCenter;
            else if (captionAlignment == ContentAlignment.TopRight || captionAlignment == ContentAlignment.MiddleRight || captionAlignment == ContentAlignment.BottomRight)
                flags |= TextFormatFlags.Right;
            if (captionAlignment == ContentAlignment.MiddleLeft || captionAlignment == ContentAlignment.MiddleCenter || captionAlignment == ContentAlignment.MiddleRight)
                flags |= TextFormatFlags.VerticalCenter;
            else if (captionAlignment == ContentAlignment.BottomLeft || captionAlignment == ContentAlignment.BottomCenter || captionAlignment == ContentAlignment.BottomRight)
                flags |= TextFormatFlags.Bottom;
            using (Font font = new Font("Arial", textPixelSize, textBold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
                TextRenderer.DrawText(e.Graphics, Text, font, rectangle, Enabled ? ForeColor : Color.FromArgb(136, 136, 136), flags);
        }

        protected override void OnMouseEnter(EventArgs e) { mouseOver = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { mouseOver = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Enabled && e.Button == MouseButtons.Left) { Focus(); mousePressed = true; Capture = true; Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            mousePressed = false; Capture = false; Invalidate(); base.OnMouseUp(e);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture) { mousePressed = false; Invalidate(); }
            base.OnMouseCaptureChanged(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { keyboardPressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { mousePressed = keyboardPressed = false; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)) { keyboardPressed = true; e.Handled = true; Invalidate(); }
            base.OnKeyDown(e);
        }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Space || key == Keys.Enter || base.IsInputKey(keyData);
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (Enabled && keyboardPressed && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            { keyboardPressed = false; e.Handled = true; Invalidate(); OnClick(EventArgs.Empty); }
            base.OnKeyUp(e);
        }
        protected override bool ProcessMnemonic(char charCode)
        {
            if (CanSelect && IsMnemonic(charCode, Text)) { Focus(); OnClick(EventArgs.Empty); return true; }
            return base.ProcessMnemonic(charCode);
        }
    }
}
