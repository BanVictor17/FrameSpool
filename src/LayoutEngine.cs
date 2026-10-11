using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace FrameSpool
{
    public sealed class PhotoLayout
    {
        public string Id, Name, Paper;
        public float Width, Height;
        public List<RectangleF> Frames = new List<RectangleF>();
        public List<LayoutImage> Images = new List<LayoutImage>();
        public List<LayoutCaption> Captions = new List<LayoutCaption>();
        public bool Info, Comments;
        public string Overlay;
        public FrameDesign Design;
        public override string ToString() { return Name; }
    }
    public sealed class LayoutImage
    {
        public string Id, Group, Direction = "0", Fitting = "FitOutside";
        public int Rotation;
    }
    public sealed class LayoutCaption
    {
        public RectangleF Bounds;
        public int PhotoIndex;
        public string Role, Value, Family = "Arial", Alignment = "Left", VerticalAlignment = "Top";
        public float Points = 8;
        public Color Color = Color.Black;
    }
    public sealed class PrintOptions
    {
        public string Printer = "", Paper = "A4", PaperType = "Ultra Glossy", Source = "Sheet", Mode = "Photo";
        public bool Borderless = true, PrintInfo, InfoFileName, ShowTime, ShowSeconds;
        public int Expansion = 1, MinimumImageSize = 60;
        public string DateFormat = "yyyy.MM.dd";
        public string DriverMode = "", DriverModePrinter = "", DriverModePaper = "";
        public bool DriverModeLandscape;
    }
    public sealed class PhotoPage
    {
        public List<PhotoItem> Photos = new List<PhotoItem>();
        public int Copies = 1;
    }
    public static class LayoutEngine
    {
        public static readonly List<PhotoLayout> Catalog = new List<PhotoLayout>();
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        public static void LoadCatalog(string path)
        { LoadCatalogJson(File.ReadAllText(path)); }
        public static void LoadEmbeddedCatalog()
        { LoadCatalogJson(AppAssets.ReadText("layouts.json")); }
        private static void LoadCatalogJson(string json)
        {
            Catalog.Clear();
            var serializer = new JavaScriptSerializer { MaxJsonLength = 16000000 };
            foreach (var item in serializer.Deserialize<List<Dictionary<string, object>>>(json))
            {
                if (!item.ContainsKey("metadata") || !item.ContainsKey("css")) continue;
                var meta = item["metadata"] as Dictionary<string, object>;
                if (meta == null || !meta.ContainsKey("PaperSize")) continue;
                string css = Convert.ToString(item["css"]);
                var page = Regex.Match(css, @"@page\s*\{([^}]+)\}").Groups[1].Value;
                var size = Regex.Match(page, @"size\s*:\s*([\d.]+)px\s+([\d.]+)px");
                if (!size.Success) continue;
                var layout = new PhotoLayout { Id = Convert.ToString(item["file"]), Name = Convert.ToString(meta["DC.Title"]), Paper = Convert.ToString(meta["PaperSize"]), Width = Parse(size.Groups[1].Value), Height = Parse(size.Groups[2].Value) };
                float ml = Property(page, "margin-left"), mt = Property(page, "margin-top");
                var containers = new Dictionary<string, RectangleF>();
                foreach (Match container in Regex.Matches(css, @"\.(container_\d+)\s*\{([^}]+)\}"))
                {
                    string rule = container.Groups[2].Value;
                    float w = Property(rule, "width"), h = Property(rule, "height");
                    if (w > 0 && h > 0) containers[container.Groups[1].Value] = new RectangleF(ml + Property(rule, "left"), mt + Property(rule, "top"), w, h);
                }
                // Only sequence-image containers accept photos. Decoration containers
                // in the shipped oval templates are not additional photo positions.
                foreach (var obj in Objects(item, "image_objects"))
                {
                    string key = Value(obj,"container");
                    if (String.IsNullOrEmpty(key)) key = "container_" + Regex.Match(Value(obj,"class"),@"Image_(\d+)").Groups[1].Value;
                    RectangleF bounds;
                    if (!containers.TryGetValue(key,out bounds)) continue;
                    var parameters = obj["params"] as Dictionary<string,object> ?? new Dictionary<string,object>();
                    layout.Frames.Add(Normalize(bounds,layout));
                    layout.Images.Add(new LayoutImage { Id=Value(obj,"id"), Group=Value(obj,"name"), Direction=Value(parameters,"Direction"), Fitting=Value(parameters,"Fitting"), Rotation=(int)Math.Round(Parse(Value(parameters,"Rotation"))/90) });
                }
                if (layout.Frames.Count == 0)
                    foreach(var bounds in containers.Values) layout.Frames.Add(Normalize(bounds,layout));
                if (layout.Frames.Count == 0) continue;
                foreach(var obj in Objects(item,"text_objects"))
                {
                    string role=Value(obj,"role");
                    if(role!="info" && role!="comment") continue;
                    var parameters=obj["params"] as Dictionary<string,object> ?? new Dictionary<string,object>();
                    string target=Value(parameters,"Target").TrimStart('#');
                    int position=layout.Images.FindIndex(x=>x.Id==target);
                    if(position<0)continue;
                    string className=Regex.Match(Value(obj,"class"),@"caption_\d+").Value;
                    string rule=Regex.Match(css,@"\."+Regex.Escape(className)+@"\s*\{([^}]+)\}").Groups[1].Value;
                    if(String.IsNullOrEmpty(rule))continue;
                    float ox=ml,oy=mt; RectangleF parent;
                    if(containers.TryGetValue(Value(obj,"container"),out parent)){ox=parent.X;oy=parent.Y;}
                    var caption=new LayoutCaption{Role=role,PhotoIndex=position,Value=Value(obj,"caption_value"),Alignment=Value(parameters,"TextAlign"),VerticalAlignment=Value(parameters,"VerticalAlign"),Bounds=Normalize(new RectangleF(ox+Property(rule,"left"),oy+Property(rule,"top"),Property(rule,"width"),Property(rule,"height")),layout)};
                    float points=Parse(Regex.Match(rule,@"font-size\s*:\s*([\d.]+)pt").Groups[1].Value);if(points>0)caption.Points=points;
                    var rgb=Regex.Match(rule,@"color\s*:\s*rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
                    if(rgb.Success)caption.Color=Color.FromArgb(Int32.Parse(rgb.Groups[1].Value),Int32.Parse(rgb.Groups[2].Value),Int32.Parse(rgb.Groups[3].Value));
                    layout.Captions.Add(caption);
                }
                layout.Info = layout.Captions.Any(c=>c.Role=="info");
                layout.Comments = layout.Captions.Any(c=>c.Role=="comment");
                Catalog.Add(layout);
            }
        }
        static IEnumerable<Dictionary<string,object>> Objects(Dictionary<string,object> item,string key)
        { object value; if(!item.TryGetValue(key,out value))yield break;var list=value as System.Collections.IEnumerable;if(list==null)yield break;foreach(var obj in list){var dict=obj as Dictionary<string,object>;if(dict!=null)yield return dict;} }
        static string Value(Dictionary<string,object> obj,string key){object value;return obj.TryGetValue(key,out value)?Convert.ToString(value):"";}
        static RectangleF Normalize(RectangleF bounds,PhotoLayout layout){return new RectangleF(bounds.X/layout.Width,bounds.Y/layout.Height,bounds.Width/layout.Width,bounds.Height/layout.Height);}
        static float Parse(string s) { float n; return float.TryParse(s, NumberStyles.Float, Invariant, out n) ? n : 0; }
        static float Property(string css, string name) { return Parse(Regex.Match(css, @"(?:^|[;\s])" + Regex.Escape(name) + @"\s*:\s*([\d.-]+)px").Groups[1].Value); }
        public static List<PhotoLayout> ForPaper(string paper)
        {
            return Catalog.Where(l => l.Paper == paper).OrderBy(l => l.Frames.Count).ThenBy(l => l.Name.StartsWith("Borderless") ? 0 : 1).ThenBy(l => l.Name).ToList();
        }
        public static List<PhotoPage> Paginate(IEnumerable<PhotoItem> photos, PhotoLayout layout)
        {
            var pages = new List<PhotoPage>();
            if (layout == null) return pages;
            var groups = layout.Images.Count==layout.Frames.Count ? layout.Images.Select((x,i)=>String.IsNullOrEmpty(x.Group)?i.ToString():x.Group).Distinct().ToList() : Enumerable.Range(0,layout.Frames.Count).Select(i=>i.ToString()).ToList();
            int used=0;
            foreach (var photo in photos)
                for (int n = 0; n < photo.Copies; n++)
                {
                    if (pages.Count == 0 || used == groups.Count)
                    { if (pages.Count == 999) return pages; pages.Add(new PhotoPage());used=0; }
                    var page=pages[pages.Count-1];
                    for(int i=0;i<layout.Frames.Count;i++)
                    {
                        string group=layout.Images.Count==layout.Frames.Count?(String.IsNullOrEmpty(layout.Images[i].Group)?i.ToString():layout.Images[i].Group):i.ToString();
                        if(group!=groups[used])continue;
                        while(page.Photos.Count<=i)page.Photos.Add(null);
                        page.Photos[i]=photo.CreatePlacement();
                    }
                    used++;
                }
            return pages;
        }
        public static RectangleF PaperBounds(RectangleF available, PhotoLayout layout)
        {
            float scale = Math.Min(available.Width / layout.Width, available.Height / layout.Height);
            return new RectangleF(available.X + (available.Width - layout.Width * scale) / 2, available.Y + (available.Height - layout.Height * scale) / 2, layout.Width * scale, layout.Height * scale);
        }
        public static void DrawPage(Graphics g, RectangleF page, PhotoLayout layout, PhotoPage content, PrintOptions options, bool corrected, PhotoItem focus)
        {
            if(layout.Design!=null){layout.Design.Draw(g,page,content,corrected,focus,false);return;}
            g.FillRectangle(Brushes.White, page);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            for (int i = 0; i < layout.Frames.Count; i++)
            {
                var f = layout.Frames[i];
                var box = new RectangleF(page.X + f.X * page.Width, page.Y + f.Y * page.Height, f.Width * page.Width, f.Height * page.Height);
                if (content != null && i < content.Photos.Count && content.Photos[i] != null)
                {
                    var photo = content.Photos[i];
                    var image = i<layout.Images.Count?layout.Images[i]:null;
                    int rotation=image==null?0:image.Rotation;
                    if(image!=null)
                    {
                        if(image.Direction=="180")rotation+=2;
                        if(image.Direction=="AutoLeft" || image.Direction=="Auto")
                        {
                            // The help documents matching the original photo orientation to
                            // the frame before applying the user's rotation. AutoLeft uses CCW.
                            bool imageLandscape=photo.Width>=photo.Height;
                            if(imageLandscape!=(box.Width>=box.Height))rotation+=3;
                        }
                    }
                    // Offsets are stored in recovered 360-dpi page units. This keeps a
                    // positioning change physically constant in preview and output.
                    PhotoEngine.DrawPhoto(g, photo, box, corrected,rotation,page.Width/layout.Width,page.Height/layout.Height);
                    if (layout.Captions.Count==0 && options.PrintInfo && layout.Info)
                    {
                        string text = options.InfoFileName ? photo.FileName : photo.CaptureDate.ToString(options.DateFormat + (options.ShowTime ? (options.ShowSeconds ? " HH:mm:ss" : " HH:mm") : ""));
                        using (var font = new Font("Arial", Math.Max(7, page.Width / 85), FontStyle.Regular, GraphicsUnit.Pixel))
                        {
                            SizeF sz = g.MeasureString(text, font);
                            var pt = new PointF(box.Right - sz.Width - 5, box.Bottom - sz.Height - 4);
                            g.DrawString(text, font, Brushes.Black, new PointF(pt.X + 1, pt.Y + 1)); g.DrawString(text, font, Brushes.Orange, pt);
                        }
                    }
                    if (layout.Captions.Count==0 && layout.Comments && photo.Comment.Length > 0)
                        using (var font = new Font("Arial", Math.Max(8, page.Width / 80), GraphicsUnit.Pixel)) g.DrawString(photo.Comment, font, Brushes.Black, box.X, box.Bottom + 2);
                    if (photo == focus)
                        using (var pen = new Pen(Color.RoyalBlue, 1)) g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                }
                else using (var pen = new Pen(Color.Silver, 1)) g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            }
            foreach(var caption in layout.Captions)
            {
                if(content==null || caption.PhotoIndex>=content.Photos.Count)continue;
                var photo=content.Photos[caption.PhotoIndex];if(photo==null)continue;
                if(caption.Role=="info"&&!options.PrintInfo)continue;
                string text=caption.Role=="comment"?photo.Comment:options.InfoFileName?photo.FileName:photo.CaptureDate.ToString(options.DateFormat+(options.ShowTime?(options.ShowSeconds?" HH:mm:ss":" HH:mm"):""));
                if(String.IsNullOrEmpty(text))continue;
                var r=caption.Bounds;var box=new RectangleF(page.X+r.X*page.Width,page.Y+r.Y*page.Height,r.Width*page.Width,r.Height*page.Height);
                using(var font=new Font(caption.Family,Math.Max(.5f,caption.Points*5*page.Width/layout.Width),FontStyle.Regular,GraphicsUnit.Pixel))
                using(var brush=new SolidBrush(caption.Color))
                using(var format=new StringFormat(StringFormat.GenericTypographic))
                {
                    format.Alignment=caption.Alignment=="Right"?StringAlignment.Far:caption.Alignment=="Center"?StringAlignment.Center:StringAlignment.Near;
                    format.LineAlignment=caption.VerticalAlignment=="Bottom"?StringAlignment.Far:caption.VerticalAlignment=="Center"?StringAlignment.Center:StringAlignment.Near;
                    format.Trimming=StringTrimming.EllipsisCharacter;
                    g.DrawString(text,font,brush,box,format);
                }
            }
            if (!String.IsNullOrEmpty(layout.Overlay) && File.Exists(layout.Overlay)) using (var overlay = Image.FromFile(layout.Overlay)) g.DrawImage(overlay, page);
        }
        public static Bitmap RenderPage(PhotoLayout layout, PhotoPage content, PrintOptions options, int dpi, bool corrected)
        {
            var bitmap = new Bitmap(Math.Max(1, (int)Math.Round(layout.Width * dpi / 360.0)), Math.Max(1, (int)Math.Round(layout.Height * dpi / 360.0)));
            bitmap.SetResolution(dpi, dpi);
            using (var g = Graphics.FromImage(bitmap)) DrawPage(g, new RectangleF(0, 0, bitmap.Width, bitmap.Height), layout, content, options, corrected, null);
            return bitmap;
        }
    }
}
