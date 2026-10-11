using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
        public List<LayoutDecoration> Decorations = new List<LayoutDecoration>();
        public List<LayoutLine> Lines = new List<LayoutLine>();
        public bool Info, Comments;
        public bool Roll, RollHorizontal;
        public float RollWidth, RollBleed;
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
        public float Rotation;
        public bool Vertical;
        public Color Color = Color.Black;
    }
    public sealed class LayoutDecoration
    {
        public RectangleF Bounds;
        public string Asset;
        public float Rotation;
    }
    public sealed class LayoutLine
    {
        public List<PointF> Points = new List<PointF>();
        public float Width = 1;
        public Color Color = Color.Black;
    }
    public sealed class PrintOptions
    {
        public string Printer = "", Paper = "A4", PaperType = "Printer Properties", Source = "", Mode = "Printer Properties";
        public bool Borderless, PrintInfo, InfoFileName, ShowTime, ShowSeconds;
        public int Expansion = 1, MinimumImageSize = 60;
        public string DateFormat = "yyyy.MM.dd";
        public string DriverMode = "", DriverModePrinter = "", DriverModePaper = "";
        public string DriverMedia="",DriverQuality="",DriverResolution="",DriverOptionsPrinter="";
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
                if(item.ContainsKey("legacy_usd"))
                {
                    var roll = item["legacy_usd"] as Dictionary<string,object>;
                    float width=Parse(Value(roll,"width")),bleed=Parse(Value(roll,"bleed"));
                    var template=new PhotoLayout{Id=Value(item,"file"),Name=Value(roll,"title"),Paper=Value(roll,"paper"),Width=width,Height=width*1.5f,Roll=true,RollHorizontal=Value(roll,"direction")=="Horizontal",RollWidth=width,RollBleed=bleed};
                    template.Frames.Add(new RectangleF(-bleed/width,-bleed/template.Height,1+2*bleed/width,1+2*bleed/template.Height));
                    template.Images.Add(new LayoutImage{Id="roll-image",Group="roll-image",Direction="AutoLeft"});
                    Catalog.Add(template);continue;
                }
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
                foreach(var obj in Objects(item,"decoration_objects"))
                {
                    RectangleF bounds;
                    if(containers.TryGetValue(Value(obj,"container"),out bounds))
                        layout.Decorations.Add(new LayoutDecoration{Bounds=Normalize(bounds,layout),Asset=Value(obj,"asset"),Rotation=Parse(Value(obj,"rotation"))});
                }
                foreach(var obj in Objects(item,"text_objects"))
                {
                    string role=Value(obj,"role");
                    var parameters=obj["params"] as Dictionary<string,object> ?? new Dictionary<string,object>();
                    if(Value(obj,"type")=="application/x-pif-line+xml")
                    {
                        var line=new LayoutLine{Width=Math.Max(.1f,Parse(Value(parameters,"LineWidth"))),Color=ParseColor(Value(parameters,"Color"))};
                        var coordinates=Value(parameters,"PointList").Split(',');
                        for(int p=0;p+1<coordinates.Length;p+=2)line.Points.Add(new PointF((ml+Parse(coordinates[p]))/layout.Width,(mt+Parse(coordinates[p+1]))/layout.Height));
                        if(line.Points.Count>=2)layout.Lines.Add(line);continue;
                    }
                    if(role=="unknown" && Value(obj,"caption_value").Contains("%E"))role="exif";
                    if(role!="info" && role!="comment" && role!="exif") continue;
                    string target=Value(parameters,"Target").TrimStart('#');
                    int position=layout.Images.FindIndex(x=>x.Id==target);
                    if(position<0)continue;
                    string className=Regex.Match(Value(obj,"class"),@"caption_\d+").Value;
                    string rule=Regex.Match(css,@"\."+Regex.Escape(className)+@"\s*\{([^}]+)\}").Groups[1].Value;
                    if(String.IsNullOrEmpty(rule))continue;
                    float ox=ml,oy=mt; RectangleF parent;
                    if(containers.TryGetValue(Value(obj,"container"),out parent)){ox=parent.X;oy=parent.Y;}
                    var caption=new LayoutCaption{Role=role,PhotoIndex=position,Value=Value(obj,"caption_value"),Alignment=Value(parameters,"TextAlign"),VerticalAlignment=Value(parameters,"VerticalAlign"),Bounds=Normalize(new RectangleF(ox+Property(rule,"left"),oy+Property(rule,"top"),Property(rule,"width"),Property(rule,"height")),layout)};
                    caption.Rotation=Parse(Value(parameters,"StrDirection"));
                    caption.Vertical=Value(parameters,"ScriptDirection")=="Vertical";
                    string family=Regex.Match(rule,@"font-family\s*:\s*[""']?([^;""',]+)").Groups[1].Value;
                    if(!String.IsNullOrWhiteSpace(family))caption.Family=family.Trim();
                    float points=Parse(Regex.Match(rule,@"font-size\s*:\s*([\d.]+)pt").Groups[1].Value);if(points>0)caption.Points=points;
                    var rgb=Regex.Match(rule,@"color\s*:\s*rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
                    if(rgb.Success)caption.Color=Color.FromArgb(Int32.Parse(rgb.Groups[1].Value),Int32.Parse(rgb.Groups[2].Value),Int32.Parse(rgb.Groups[3].Value));
                    layout.Captions.Add(caption);
                }
                layout.Info = layout.Captions.Any(c=>c.Role=="info"||c.Role=="exif");
                layout.Comments = layout.Captions.Any(c=>c.Role=="comment");
                Catalog.Add(layout);
            }
        }
        static IEnumerable<Dictionary<string,object>> Objects(Dictionary<string,object> item,string key)
        { object value; if(!item.TryGetValue(key,out value))yield break;var list=value as System.Collections.IEnumerable;if(list==null)yield break;foreach(var obj in list){var dict=obj as Dictionary<string,object>;if(dict!=null)yield return dict;} }
        static string Value(Dictionary<string,object> obj,string key){object value;return obj.TryGetValue(key,out value)?Convert.ToString(value):"";}
        static Color ParseColor(string value){var parts=value.Split(',');if(parts.Length<3)return Color.Black;return Color.FromArgb(Math.Max(0,Math.Min(255,(int)Parse(parts[0]))),Math.Max(0,Math.Min(255,(int)Parse(parts[1]))),Math.Max(0,Math.Min(255,(int)Parse(parts[2]))));}
        static RectangleF Normalize(RectangleF bounds,PhotoLayout layout){return new RectangleF(bounds.X/layout.Width,bounds.Y/layout.Height,bounds.Width/layout.Width,bounds.Height/layout.Height);}
        static float Parse(string s) { float n; return float.TryParse(s, NumberStyles.Float, Invariant, out n) ? n : 0; }
        static float Property(string css, string name) { return Parse(Regex.Match(css, @"(?:^|[;\s])" + Regex.Escape(name) + @"\s*:\s*([\d.-]+)px").Groups[1].Value); }
        public static List<PhotoLayout> ForPaper(string paper)
        {
            return Catalog.Where(l => l.Paper == paper).OrderBy(l => l.Frames.Count).ThenBy(l => l.Name.StartsWith("Borderless") ? 0 : 1).ThenBy(l => l.Name).ToList();
        }
        // Roll definitions encode the printable width and requested photo direction,
        // with length zero meaning that the photo determines the paper length.
        public static PhotoLayout ResolveForPhotos(PhotoLayout layout,IEnumerable<PhotoItem> photos)
        {
            var photo=photos==null?null:photos.FirstOrDefault(p=>p!=null&&p.Copies>0);
            return ResolveRoll(layout,photo);
        }
        static PhotoLayout ResolveRoll(PhotoLayout layout,PhotoItem photo)
        {
            if(layout==null||!layout.Roll)return layout;
            float ratio=photo==null?1.5f:(float)Math.Max(photo.Width,photo.Height)/Math.Max(1,Math.Min(photo.Width,photo.Height));
            if(layout.RollHorizontal)ratio=1/ratio;
            float height=Math.Max(360,Math.Min(30000,layout.RollWidth*ratio));
            var result=new PhotoLayout{Id=layout.Id,Name=layout.Name,Paper=layout.Paper,Width=layout.RollWidth,Height=height,Roll=true,RollHorizontal=layout.RollHorizontal,RollWidth=layout.RollWidth,RollBleed=layout.RollBleed};
            result.Frames.Add(new RectangleF(-layout.RollBleed/result.Width,-layout.RollBleed/height,1+2*layout.RollBleed/result.Width,1+2*layout.RollBleed/height));
            result.Images.AddRange(layout.Images);return result;
        }
        public static PhotoLayout ResolveForPage(PhotoLayout layout,PhotoPage page)
        {return ResolveRoll(layout,page==null?null:page.Photos.FirstOrDefault(p=>p!=null));}
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
            if(layout.Design!=null){layout.Design.Draw(g,page,content,corrected,focus,false,options);return;}
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
                if((caption.Role=="info"||caption.Role=="exif")&&!options.PrintInfo)continue;
                string text=CaptionText(caption.Role,caption.Value,photo,options);
                if(String.IsNullOrEmpty(text))continue;
                var r=caption.Bounds;var box=new RectangleF(page.X+r.X*page.Width,page.Y+r.Y*page.Height,r.Width*page.Width,r.Height*page.Height);
                DrawCaption(g,box,text,caption,page.Width/layout.Width);
            }
            foreach(var line in layout.Lines)
                using(var pen=new Pen(line.Color,Math.Max(.1f,line.Width*page.Width/layout.Width)))
                    g.DrawLines(pen,line.Points.Select(p=>new PointF(page.X+p.X*page.Width,page.Y+p.Y*page.Height)).ToArray());
            foreach(var decoration in layout.Decorations)
            {
                if(!AppAssets.Contains(decoration.Asset))continue;
                var r=decoration.Bounds;var box=new RectangleF(page.X+r.X*page.Width,page.Y+r.Y*page.Height,r.Width*page.Width,r.Height*page.Height);
                var state=g.Save();
                try
                {
                    if(decoration.Rotation!=0){g.TranslateTransform(box.X+box.Width/2,box.Y+box.Height/2);g.RotateTransform(decoration.Rotation);g.TranslateTransform(-box.X-box.Width/2,-box.Y-box.Height/2);}
                    using(var stream=AppAssets.Open(decoration.Asset))using(var image=Image.FromStream(stream))using(var attributes=new ImageAttributes())
                    {attributes.SetWrapMode(WrapMode.TileFlipXY);g.DrawImage(image,new[]{new PointF(box.Left,box.Top),new PointF(box.Right,box.Top),new PointF(box.Left,box.Bottom)},new RectangleF(0,0,image.Width,image.Height),GraphicsUnit.Pixel,attributes);}
                }
                finally{g.Restore(state);}
            }
            if (!String.IsNullOrEmpty(layout.Overlay) && File.Exists(layout.Overlay)) using (var overlay = Image.FromFile(layout.Overlay)) g.DrawImage(overlay, page);
        }
        internal static string CaptionText(string role,string value,PhotoItem photo,PrintOptions options)
        {
            if(photo==null)return "";
            if(role=="comment")return photo.Comment;
            if(role=="exif")
            {
                string result=String.IsNullOrEmpty(value)?"%E %U %I %C":value;
                foreach(var token in new[]{new[]{"%E","EXP Time"},new[]{"%U","F Number"},new[]{"%I","ISO Sensitivity"},new[]{"%C","Camera"}})
                {string text;if(photo.Metadata==null||!photo.Metadata.TryGetValue(token[1],out text)||String.IsNullOrWhiteSpace(text)||text=="-")text="";result=result.Replace(token[0],text);}
                return Regex.Replace(result,@"\s+"," ").Trim();
            }
            if(options.InfoFileName)return photo.FileName;
            try{return photo.CaptureDate.ToString(options.DateFormat+(options.ShowTime?(options.ShowSeconds?" HH:mm:ss":" HH:mm"):""));}
            catch(FormatException){return photo.CaptureDate.ToString("yyyy.MM.dd");}
        }
        internal static void DrawCaption(Graphics graphics,RectangleF box,string text,LayoutCaption caption,float pageScale)
        {
            if(box.Width<=0||box.Height<=0||String.IsNullOrEmpty(text))return;
            var state=graphics.Save();
            try
            {
                graphics.SetClip(box,CombineMode.Intersect);
                if(caption.Rotation!=0){graphics.TranslateTransform(box.X+box.Width/2,box.Y+box.Height/2);graphics.RotateTransform(caption.Rotation);graphics.TranslateTransform(-box.X-box.Width/2,-box.Y-box.Height/2);}
                using(var font=CaptionFont(caption.Family,Math.Max(.5f,caption.Points*5*pageScale)))
                using(var brush=new SolidBrush(caption.Color))
                using(var format=new StringFormat(StringFormat.GenericTypographic))
                {
                    format.Alignment=caption.Alignment=="Right"?StringAlignment.Far:caption.Alignment=="Center"?StringAlignment.Center:StringAlignment.Near;
                    format.LineAlignment=caption.VerticalAlignment=="Bottom"?StringAlignment.Far:caption.VerticalAlignment=="Center"?StringAlignment.Center:StringAlignment.Near;
                    format.Trimming=StringTrimming.EllipsisCharacter;
                    if(caption.Vertical)format.FormatFlags|=StringFormatFlags.DirectionVertical;
                    graphics.DrawString(text,font,brush,box,format);
                }
            }
            finally{graphics.Restore(state);}
        }
        static Font CaptionFont(string family,float pixels)
        {
            // Preserve the declared face when installed. The shipped generic Gothic
            // alias has no Windows font mapping in the template; Arial is a stable
            // western fallback rather than silently accepting GDI+'s default face.
            try{using(var face=new FontFamily(family))return new Font(face,pixels,FontStyle.Regular,GraphicsUnit.Pixel);}
            catch(ArgumentException){return new Font("Arial",pixels,FontStyle.Regular,GraphicsUnit.Pixel);}
        }
        public static Bitmap RenderPage(PhotoLayout layout, PhotoPage content, PrintOptions options, int dpi, bool corrected)
        {
            var bitmap = new Bitmap(Math.Max(1, (int)Math.Round(layout.Width * dpi / 360.0)), Math.Max(1, (int)Math.Round(layout.Height * dpi / 360.0)));
            try
            {
                bitmap.SetResolution(dpi, dpi);
                using (var g = Graphics.FromImage(bitmap)) DrawPage(g, new RectangleF(0, 0, bitmap.Width, bitmap.Height), layout, content, options, corrected, null);
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
    }
}
