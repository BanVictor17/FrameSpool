using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FrameSpool
{
    public partial class PagePreview
    {
        sealed class Snapshot
        {
            public string Key;
            public PhotoLayout Layout;
            public PhotoPage Page;
            public PrintOptions Options;
            public int Dpi;
            public bool Corrected;
            public Bitmap Render(){return FrameSpool.LayoutEngine.RenderPage(Layout,Page,Options,Dpi,Corrected);}
        }
        sealed class CachedPage { public Bitmap Image;public long Used; }
        readonly Dictionary<string,CachedPage> pageCache=new Dictionary<string,CachedPage>();
        readonly HashSet<string> preloading=new HashSet<string>();
        readonly List<Tuple<PhotoLayout,PhotoPage>> adjacent=new List<Tuple<PhotoLayout,PhotoPage>>();
        const long CacheBudget=64*1024*1024;
        long cacheClock;
        string requestedKey,displayedKey;
        volatile bool cacheClosed;
        internal bool HasDisplayedPreview {get{return renderer.Image!=null;}}
        internal bool CurrentPreviewDisplayed {get{return displayedKey==requestedKey&&renderer.Image!=null;}}
        internal int CachedPages {get{return pageCache.Count;}}
        internal long CachedBytes {get{return pageCache.Values.Sum(c=>(long)c.Image.Width*c.Image.Height*4);}}
        internal Color PreviewCenterColor {get{return renderer.Image==null?Color.Empty:renderer.Image.GetPixel(renderer.Image.Width/2,renderer.Image.Height/2);}}
        public void SetAdjacent(IEnumerable<Tuple<PhotoLayout,PhotoPage>> pages)
        {adjacent.Clear();adjacent.AddRange(pages.Take(2));}
        Snapshot CaptureSnapshot(PhotoLayout layout,PhotoPage page)
        {
            var layoutCopy=new PhotoLayout();foreach(var field in typeof(PhotoLayout).GetFields())field.SetValue(layoutCopy,field.GetValue(layout));
            layoutCopy.Frames=new List<RectangleF>(layout.Frames);layoutCopy.Design=layout.Design?.Clone();
            var copy=page==null?null:new PhotoPage{Photos=page.Photos.Select(p=>p?.Clone()).ToList()};
            var options=new PrintOptions();if(Options!=null)foreach(var field in typeof(PrintOptions).GetFields())field.SetValue(options,field.GetValue(Options));
            var sheet=fitToWindow?FrameSpool.LayoutEngine.PaperBounds(new RectangleF(12,10,Math.Max(1,ClientSize.Width-24),Math.Max(1,ClientSize.Height-22)),layoutCopy):new RectangleF(0,0,layoutCopy.Width*96*Ui.Scale(this)/360,layoutCopy.Height*96*Ui.Scale(this)/360);
            int dpi=(int)Math.Ceiling(Math.Min(144,Math.Max(24,Math.Max(sheet.Width*360/layoutCopy.Width,sheet.Height*360/layoutCopy.Height))));
            var state=new {Layout=layoutCopy,Dpi=dpi,Corrected,options.PrintInfo,options.InfoFileName,options.DateFormat,options.ShowTime,options.ShowSeconds,
                Photos=copy?.Photos.Select(p=>p==null?null:new {p.FilePath,p.Rotation,p.Zoom,p.OffsetX,p.OffsetY,p.Fit,p.Comment,p.CaptureDate,p.Metadata,p.Adjustments,Stamp=Stamp(p.FilePath)}).ToArray(),
                OverlayStamp=Stamp(layoutCopy.Overlay),Artwork=layoutCopy.Design?.Elements.Where(e=>!String.IsNullOrEmpty(e.ImagePath)).Select(e=>Stamp(e.ImagePath)).ToArray()};
            string json=JsonSerializer.Serialize(state,new JsonSerializerOptions{IncludeFields=true});
            return new Snapshot{Key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))),Layout=layoutCopy,Page=copy,Options=options,Dpi=dpi,Corrected=Corrected};
        }
        static string Stamp(string path){try{var file=new FileInfo(path);return file.Exists?file.Length+":"+file.LastWriteTimeUtc.Ticks:"missing";}catch{return "unavailable";}}
        void Remember(string key,Bitmap image)
        {
            if(key==null||image==null||cacheClosed)return;
            if(pageCache.TryGetValue(key,out var existing)){existing.Used=++cacheClock;return;}
            pageCache[key]=new CachedPage{Image=(Bitmap)image.Clone(),Used=++cacheClock};
            while(pageCache.Count>8||pageCache.Values.Sum(c=>(long)c.Image.Width*c.Image.Height*4)>CacheBudget)
            {var candidates=pageCache.Where(c=>c.Key!=requestedKey).ToList();var oldest=(candidates.Count>0?candidates:pageCache.ToList()).OrderBy(c=>c.Value.Used).First();oldest.Value.Image.Dispose();pageCache.Remove(oldest.Key);}
        }
        void PreviewChanged()
        {
            if(cacheClosed)return;displayedKey=requestedKey;
            Remember(displayedKey,renderer.Image);Invalidate();
            foreach(var item in adjacent)if(item.Item1!=null)Preload(CaptureSnapshot(item.Item1,item.Item2));
        }
        async void Preload(Snapshot snapshot)
        {
            if(cacheClosed||preloading.Count>=2||pageCache.ContainsKey(snapshot.Key)||!preloading.Add(snapshot.Key))return;
            Bitmap result=null;
            try{result=await PreviewRenderer.Decode(()=>cacheClosed?null:snapshot.Render());if(!cacheClosed){Remember(snapshot.Key,result);if(snapshot.Key==requestedKey&&!CurrentPreviewDisplayed&&result!=null)renderer.Display((Bitmap)result.Clone());}}
            catch{ /* Visiting the page renders again and reports any real error. */ }
            finally{result?.Dispose();preloading.Remove(snapshot.Key);}
        }
        void DisposeCache()
        {cacheClosed=true;foreach(var entry in pageCache.Values)entry.Image.Dispose();pageCache.Clear();adjacent.Clear();}
        void RequestPagePreview()
        {
            if(positioningPhoto!=null&&!PositioningCurrent)EndPhotoPositioning();
            if(LayoutDefinition==null||IsDisposed)return;
            var snapshot=CaptureSnapshot(LayoutDefinition,Page);bool navigating=renderedPage!=Page||renderedLayout!=LayoutDefinition;
            renderedPage=Page;renderedLayout=LayoutDefinition;
            if(snapshot.Key==requestedKey&&renderer.Error==null){if(CurrentPreviewDisplayed)foreach(var item in adjacent)if(item.Item1!=null)Preload(CaptureSnapshot(item.Item1,item.Item2));return;}
            requestedKey=snapshot.Key;
            if(pageCache.TryGetValue(snapshot.Key,out var cached))
            {cached.Used=++cacheClock;renderer.Display((Bitmap)cached.Image.Clone());}
            else renderer.Request(snapshot.Render,false,navigating||positioningPhoto!=null&&IsDragging);
        }
    }
}
