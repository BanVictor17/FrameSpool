using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FrameSpool
{
    public static class Ui
    {
        public static float Scale(Control control) { var form=control.FindForm() as DpiForm;return form==null?control.DeviceDpi/96f:form.UiScale; }
        public static Font Font(float pixels) { return new Font("Arial", pixels, FontStyle.Regular, GraphicsUnit.Pixel); }
        public static Label Label(Control parent, string text, int x, int y, int w, int h)
        { var c = new Label { Text = text, Bounds = new Rectangle(x, y, w, h), Font = Font(12), BackColor = Color.Transparent, AutoEllipsis = true }; parent.Controls.Add(c); return c; }
        public static Button Button(Control parent, string text, int x, int y, int w, int h, Action click)
        { var c = new Button { Text = text, Bounds = new Rectangle(x, y, w, h), Font = Font(12) }; c.Click += delegate { click(); }; parent.Controls.Add(c); return c; }
        public static SkinButton Skin(Control parent, string text, string image, int x, int y, int w, int h, Action click)
        { var c = new SkinButton(text, image) { Bounds = new Rectangle(x, y, w, h) }; c.Click += delegate { click(); }; parent.Controls.Add(c); return c; }
        public static CheckBox Check(Control parent, string text, int x, int y, int w, bool value, Action<bool> changed)
        { var c = new CheckBox { Text = text, Bounds = new Rectangle(x, y, w, 20), Font = Font(12), Checked = value }; if (changed != null) c.CheckedChanged += delegate { changed(c.Checked); }; parent.Controls.Add(c); return c; }
        public static ComboBox Combo(Control parent, string[] items, int x, int y, int w, string selected, Action<string> changed)
        { var c = new ComboBox { Bounds = new Rectangle(x, y, w, 24), Font = Font(12), DropDownStyle = ComboBoxStyle.DropDownList }; c.Items.AddRange(items); if (selected != null && c.Items.Contains(selected)) c.SelectedItem = selected; else if(c.Items.Count>0)c.SelectedIndex=0; if(changed!=null)c.SelectedIndexChanged += delegate { changed(c.Text); }; parent.Controls.Add(c); return c; }
        public static NumericUpDown Number(Control parent, int x, int y, int w, int min, int max, decimal value, Action<int> changed)
        { var c = new NumericUpDown { Bounds = new Rectangle(x, y, w, 24), Font = Font(12), Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)) }; if(changed!=null)c.ValueChanged += delegate { changed((int)c.Value); }; parent.Controls.Add(c); return c; }
        public static Form Dialog(string title, int width, int height)
        { return new DpiForm { Text = title, ClientSize = new Size(width, height), Font = Font(12), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(252,252,254), AutoScaleMode = AutoScaleMode.None }; }
    }
    public class TileView : ScrollableControl
    {
        public List<PhotoItem> Photos = new List<PhotoItem>();
        public PhotoItem FocusPhoto;
        public bool SelectionList;
        public bool OneClickSelection = true;
        public bool AllowPhotoDrag;
        public int TileSize = 130;
        public event Action<PhotoItem> PhotoFocused, PhotoEnlarged, CopiesChanged;
        readonly Dictionary<string, Bitmap> thumbs = new Dictionary<string, Bitmap>();
        readonly HashSet<string> pending = new HashSet<string>(), failed = new HashSet<string>();
        volatile bool closed;
        internal bool ThumbnailsReady { get { return pending.Count == 0; } }
        static string Key(PhotoItem photo) { return photo.FilePath + "|" + photo.Rotation; }
        async void LoadThumbnail(PhotoItem photo)
        {
            string key=Key(photo); if(closed||pending.Contains(key)||failed.Contains(key))return;
            pending.Add(key); var snapshot=photo.Clone(); Bitmap image=null;
            try { image=await PreviewRenderer.Decode(()=>closed?null:PhotoEngine.CreateThumbnail(snapshot,180,180)); } catch { if(!closed)failed.Add(key); }
            pending.Remove(key);
            if(closed||!Photos.Any(p=>Key(p)==key)){if(image!=null)image.Dispose();return;}
            if(image!=null)thumbs[key]=image; Invalidate();
        }
        PhotoItem dragPhoto;Point dragStart;
        public TileView()
        {
            DoubleBuffered = true; AutoScroll = true; BackColor = Color.White; TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }
        int D(int n){return (int)Math.Round(n*Ui.Scale(this));}
        int Columns { get { return SelectionList ? 1 : Math.Max(1, (ClientSize.Width-D(20)) / (D(TileSize)+D(8))); } }
        int CardWidth { get { return SelectionList ? Math.Max(D(85),ClientSize.Width-D(22)) : D(TileSize); } }
        int CardHeight { get { return SelectionList ? Math.Max(D(100),CardWidth+D(20)) : D(TileSize+2); } }
        public void RefreshPhotos() {
            var present=new HashSet<string>(Photos.Select(p=>Key(p)),StringComparer.OrdinalIgnoreCase);
            foreach(var key in thumbs.Keys.Where(k=>!present.Contains(k)).ToList()){thumbs[key].Dispose();thumbs.Remove(key);}
            AutoScrollMinSize = new Size(0, ((Photos.Count + Columns-1)/Columns)*(CardHeight+D(8))+D(8)); Invalidate();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); AutoScrollMinSize = new Size(0, ((Photos.Count + Columns-1)/Columns)*(CardHeight+D(8))+D(8)); Invalidate(); }
        Rectangle Card(int index) { return new Rectangle(D(5)+(index%Columns)*(CardWidth+D(8)), D(5)+(index/Columns)*(CardHeight+D(8))+AutoScrollPosition.Y, CardWidth,CardHeight); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.Clear(BackColor); e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // Retain only visible thumbnails and a small working set while scrolling.
            if(thumbs.Count>96){var visible=new HashSet<string>(Photos.Where((p,i)=>Card(i).Bottom>=0&&Card(i).Top<=Height).Select(p=>Key(p)));foreach(var key in thumbs.Keys.Where(k=>!visible.Contains(k)).ToList()){thumbs[key].Dispose();thumbs.Remove(key);}}
            using (var font = Ui.Font(10*Ui.Scale(this)))
            for(int i=0;i<Photos.Count;i++)
            {
                var p=Photos[i]; var r=Card(i); if(r.Bottom<0 || r.Top>Height)continue;
                using(var fill=new SolidBrush(p.Copies>0&&!SelectionList?Color.FromArgb(130,177,244):Color.FromArgb(225,226,227))) e.Graphics.FillRectangle(fill,r);
                using(var pen=new Pen(p==FocusPhoto?Color.RoyalBlue:Color.Silver,p==FocusPhoto?2:1)) e.Graphics.DrawRectangle(pen,r.X,r.Y,r.Width,r.Height);
                var imageBox=new Rectangle(r.X+D(5),r.Y+D(5),r.Width-D(10),r.Height-D(29));
                e.Graphics.FillRectangle(Brushes.White,imageBox);
                Bitmap image; if(!thumbs.TryGetValue(Key(p),out image))LoadThumbnail(p);
                if(image!=null) { float scale=Math.Min((float)imageBox.Width/image.Width,(float)imageBox.Height/image.Height); var dest=new RectangleF(imageBox.X+(imageBox.Width-image.Width*scale)/2,imageBox.Y+(imageBox.Height-image.Height*scale)/2,image.Width*scale,image.Height*scale); e.Graphics.DrawImage(image,dest); }
                TextRenderer.DrawText(e.Graphics,p.FileName,font,new Rectangle(r.X+D(4),r.Bottom-D(20),r.Width-D(30),D(17)),Color.Black,TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(e.Graphics,"x "+p.Copies,font,new Rectangle(r.Right-D(30),r.Bottom-D(20),D(28),D(17)),Color.Black,TextFormatFlags.Right);
                if(p==FocusPhoto&&!SelectionList)
                {
                    e.Graphics.FillRectangle(Brushes.White,r.X+D(5),r.Bottom-D(25),r.Width-D(10),D(21));
                    using(var brush=new SolidBrush(Color.FromArgb(62,76,84))) { e.Graphics.FillRectangle(brush,r.X+D(7),r.Bottom-D(23),D(31),D(17)); e.Graphics.FillRectangle(brush,r.Right-D(38),r.Bottom-D(23),D(31),D(17)); }
                    using(var symbolFont=Ui.Font(16*Ui.Scale(this))){TextRenderer.DrawText(e.Graphics,"−",symbolFont,new Rectangle(r.X+D(7),r.Bottom-D(25),D(31),D(20)),Color.White,TextFormatFlags.HorizontalCenter);
                    TextRenderer.DrawText(e.Graphics,"+",symbolFont,new Rectangle(r.Right-D(38),r.Bottom-D(25),D(31),D(20)),Color.White,TextFormatFlags.HorizontalCenter);}
                    TextRenderer.DrawText(e.Graphics,p.Copies.ToString(),font,new Rectangle(r.X+D(39),r.Bottom-D(23),r.Width-D(78),D(18)),Color.Black,TextFormatFlags.HorizontalCenter);
                }
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            for(int i=0;i<Photos.Count;i++) { var r=Card(i); if(!r.Contains(e.Location))continue; var p=Photos[i]; bool wasFocused=p==FocusPhoto; FocusPhoto=p; if(PhotoFocused!=null)PhotoFocused(p);
                if(e.Button==MouseButtons.Left && !SelectionList) { if(wasFocused&&e.Y>=r.Bottom-D(25)) { if(e.X<=r.X+D(38))p.Copies=Math.Max(0,p.Copies-1); else if(e.X>=r.Right-D(38))p.Copies=Math.Min(99,p.Copies+1); } else if(p.Copies==0&&OneClickSelection)p.Copies=1; if(CopiesChanged!=null)CopiesChanged(p); }
                if(e.Button==MouseButtons.Left&&(SelectionList||AllowPhotoDrag)){dragPhoto=p;dragStart=e.Location;}
                Invalidate(); break;
            }
        }
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragPhoto==null||e.Button!=MouseButtons.Left||new Rectangle(dragStart.X-SystemInformation.DragSize.Width/2,dragStart.Y-SystemInformation.DragSize.Height/2,SystemInformation.DragSize.Width,SystemInformation.DragSize.Height).Contains(e.Location))return;var photo=dragPhoto;dragPhoto=null;DoDragDrop(photo.FilePath,DragDropEffects.Copy);}
        protected override void OnMouseUp(MouseEventArgs e){dragPhoto=null;base.OnMouseUp(e);}
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); if(FocusPhoto!=null&&PhotoEnlarged!=null)PhotoEnlarged(FocusPhoto); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if(FocusPhoto==null)return; if(e.KeyCode==Keys.Add||e.KeyCode==Keys.Oemplus)FocusPhoto.Copies=Math.Min(99,FocusPhoto.Copies+1); else if(e.KeyCode==Keys.Subtract||e.KeyCode==Keys.OemMinus)FocusPhoto.Copies=Math.Max(0,FocusPhoto.Copies-1); else if(e.KeyCode==Keys.Enter&&PhotoEnlarged!=null)PhotoEnlarged(FocusPhoto); if(CopiesChanged!=null)CopiesChanged(FocusPhoto); Invalidate(); }
        protected override void Dispose(bool disposing) { if(disposing){closed=true;foreach(var image in thumbs.Values)image.Dispose();thumbs.Clear();if(ContextMenuStrip!=null){ContextMenuStrip.Dispose();ContextMenuStrip=null;}} base.Dispose(disposing); }
    }
    public partial class PagePreview : ScrollableControl
    {
        public PhotoLayout LayoutDefinition;
        public PhotoPage Page;
        public PrintOptions Options;
        public PhotoItem FocusPhoto;
        public bool Corrected;
        public bool EnableManual;
        bool fitToWindow=true;
        int pressedSlot=-1, contextSlot=-1;
        Point pressedPoint;
        readonly PreviewRenderer renderer=new PreviewRenderer();
        readonly ContextMenuStrip slotMenu=new ContextMenuStrip();
        PhotoPage renderedPage;
        PhotoLayout renderedLayout;
        internal bool RenderingReady { get { return renderer.Ready; } }
        public bool IsDragging { get; private set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool FitToWindow{get{return fitToWindow;}set{fitToWindow=value;RefreshScale();}}
        public event Action<PhotoItem> PhotoFocused;
        public event Action<int,string> PhotoDropped;
        public event Action<int,int> PhotoMoved;
        public event Action<int> SlotCleared;
        public PagePreview()
        {
            DoubleBuffered=true; SetStyle(ControlStyles.ResizeRedraw,true);
            BackColor=Color.FromArgb(235,236,238);AllowDrop=true;AutoScroll=true;
            renderer.Changed+=PreviewChanged;
            slotMenu.Items.Add("Clear Settings",null,delegate{if(EnableManual&&Page!=null&&contextSlot>=0&&contextSlot<Page.Photos.Count&&SlotCleared!=null)SlotCleared(contextSlot);});
        }
        public void RefreshScale()
        {
            AutoScrollMinSize=LayoutDefinition==null||fitToWindow?Size.Empty:new Size((int)(LayoutDefinition.Width*96*Ui.Scale(this)/360)+24,(int)(LayoutDefinition.Height*96*Ui.Scale(this)/360)+22);
            QueueRender();Invalidate();
        }
        protected override void OnResize(EventArgs e){base.OnResize(e);if(renderer!=null)QueueRender();}
        void QueueRender()
        {
            RequestPagePreview();
        }
        public RectangleF Sheet { get { if(LayoutDefinition==null)return RectangleF.Empty;if(fitToWindow)return FrameSpool.LayoutEngine.PaperBounds(new RectangleF(12,10,Math.Max(1,ClientSize.Width-24),Math.Max(1,ClientSize.Height-22)),LayoutDefinition);float w=LayoutDefinition.Width*96*Ui.Scale(this)/360,h=LayoutDefinition.Height*96*Ui.Scale(this)/360;return new RectangleF(Math.Max(12,(ClientSize.Width-w)/2)+AutoScrollPosition.X,Math.Max(10,(ClientSize.Height-h)/2)+AutoScrollPosition.Y,w,h); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);if(LayoutDefinition==null)return;var sheet=Sheet;
            e.Graphics.FillRectangle(Brushes.Gray,sheet.X+3,sheet.Y+3,sheet.Width,sheet.Height);e.Graphics.FillRectangle(Brushes.White,sheet);
            if(renderer.Image!=null)e.Graphics.DrawImage(renderer.Image,sheet);
            if(renderer.Error!=null)TextRenderer.DrawText(e.Graphics,"Preview unavailable: "+renderer.Error,Font,Rectangle.Round(sheet),Color.DimGray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
            if(CurrentPreviewDisplayed&&Page!=null)for(int i=0;i<LayoutDefinition.Frames.Count&&i<Page.Photos.Count;i++)if(Page.Photos[i]!=null&&Page.Photos[i]==FocusPhoto)
            {using(var path=SlotPath(i))using(var pen=new Pen(Color.RoyalBlue,2))e.Graphics.DrawPath(pen,path);}
        }
        RectangleF SlotBounds(int i){var sheet=Sheet;var f=LayoutDefinition.Frames[i];return new RectangleF(sheet.X+f.X*sheet.Width,sheet.Y+f.Y*sheet.Height,f.Width*sheet.Width,f.Height*sheet.Height);}
        GraphicsPath SlotPath(int i)
        {
            var box=SlotBounds(i);var path=new GraphicsPath();
            var opening=LayoutDefinition.Design==null?null:LayoutDefinition.Design.Elements.Where(FrameDesign.IsOpening).ElementAtOrDefault(i);
            if(opening!=null&&opening.Kind=="Ellipse Opening")path.AddEllipse(box);else path.AddRectangle(box);
            if(opening!=null&&opening.Rotation!=0)using(var transform=new Matrix()){transform.RotateAt(opening.Rotation,new PointF(box.Left+box.Width/2,box.Top+box.Height/2));path.Transform(transform);}
            return path;
        }
        internal int At(Point point) { if(LayoutDefinition==null||!Sheet.Contains(point))return -1;for(int i=LayoutDefinition.Frames.Count-1;i>=0;i--)using(var path=SlotPath(i))if(path.IsVisible(point))return i;return -1; }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);Focus();int i=At(e.Location);pressedSlot=-1;
            if(Page==null||i<0||i>=Page.Photos.Count||Page.Photos[i]==null)return;
            FocusPhoto=Page.Photos[i];if(PhotoFocused!=null)PhotoFocused(FocusPhoto);
            if(EnableManual&&e.Button==MouseButtons.Left){pressedSlot=i;pressedPoint=e.Location;}
            if(EnableManual&&e.Button==MouseButtons.Right){contextSlot=i;slotMenu.Show(this,e.Location);}Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);if(!EnableManual||pressedSlot<0||e.Button!=MouseButtons.Left)return;
            var size=SystemInformation.DragSize;if(new Rectangle(pressedPoint.X-size.Width/2,pressedPoint.Y-size.Height/2,size.Width,size.Height).Contains(e.Location))return;
            int slot=pressedSlot;pressedSlot=-1;var data=new DataObject();data.SetData("FrameSpool.Slot",slot);data.SetData("FrameSpool.Page",Page);IsDragging=true;
            try{DoDragDrop(data,DragDropEffects.Move);}finally{IsDragging=false;}
        }
        protected override void OnMouseUp(MouseEventArgs e){pressedSlot=-1;base.OnMouseUp(e);}
        internal DragDropEffects DropEffect(IDataObject data,Point point)
        {
            if(!EnableManual||data==null||At(point)<0)return DragDropEffects.None;
            if(data.GetDataPresent("FrameSpool.Slot"))
            {var value=data.GetData("FrameSpool.Slot");if(!(value is int)||!Object.ReferenceEquals(data.GetData("FrameSpool.Page"),Page)||Page==null)return DragDropEffects.None;int slot=(int)value;return slot>=0&&slot<Page.Photos.Count&&Page.Photos[slot]!=null?DragDropEffects.Move:DragDropEffects.None;}
            if(data.GetDataPresent(DataFormats.FileDrop)){var paths=data.GetData(DataFormats.FileDrop) as string[];return paths!=null&&paths.Length>0?DragDropEffects.Copy:DragDropEffects.None;}
            return data.GetDataPresent(DataFormats.Text)&&!String.IsNullOrWhiteSpace(data.GetData(DataFormats.Text) as string)?DragDropEffects.Copy:DragDropEffects.None;
        }
        protected override void OnDragEnter(DragEventArgs e){base.OnDragEnter(e);e.Effect=DropEffect(e.Data,PointToClient(new Point(e.X,e.Y)));}
        protected override void OnDragOver(DragEventArgs e){base.OnDragOver(e);e.Effect=DropEffect(e.Data,PointToClient(new Point(e.X,e.Y)));}
        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);var point=PointToClient(new Point(e.X,e.Y));e.Effect=DropEffect(e.Data,point);if(e.Effect==DragDropEffects.None)return;int i=At(point);
            if(e.Effect==DragDropEffects.Move){if(PhotoMoved!=null)PhotoMoved((int)e.Data.GetData("FrameSpool.Slot"),i);return;}
            string path=e.Data.GetDataPresent(DataFormats.FileDrop)?((string[])e.Data.GetData(DataFormats.FileDrop))[0]:(string)e.Data.GetData(DataFormats.Text);if(PhotoDropped!=null)PhotoDropped(i,path);
        }
        protected override void Dispose(bool disposing){if(disposing){DisposeCache();renderer.Dispose();slotMenu.Dispose();}base.Dispose(disposing);}
    }
}
