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
        { return new Form { Text = title, ClientSize = new Size(width, height), Font = Font(12), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(252,252,254), AutoScaleMode = AutoScaleMode.None }; }
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
        PhotoItem dragPhoto;Point dragStart;
        public TileView()
        {
            DoubleBuffered = true; AutoScroll = true; BackColor = Color.White; TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }
        int Columns { get { return SelectionList ? 1 : Math.Max(1, (ClientSize.Width-20) / (TileSize+8)); } }
        int CardWidth { get { return SelectionList ? Math.Max(85,ClientSize.Width-22) : TileSize; } }
        int CardHeight { get { return SelectionList ? Math.Max(100,CardWidth+20) : TileSize+2; } }
        public void RefreshPhotos() {
            var present=new HashSet<string>(Photos.Select(p=>p.FilePath),StringComparer.OrdinalIgnoreCase);
            foreach(var key in thumbs.Keys.Where(k=>!present.Contains(k)).ToList()){thumbs[key].Dispose();thumbs.Remove(key);}
            AutoScrollMinSize = new Size(0, ((Photos.Count + Columns-1)/Columns)*(CardHeight+8)+8); Invalidate();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); RefreshPhotos(); }
        Rectangle Card(int index) { return new Rectangle(5+(index%Columns)*(CardWidth+8), 5+(index/Columns)*(CardHeight+8)+AutoScrollPosition.Y, CardWidth,CardHeight); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.Clear(BackColor); e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // Retain only visible thumbnails and a small working set while scrolling.
            if(thumbs.Count>96){var visible=new HashSet<string>(Photos.Where((p,i)=>Card(i).Bottom>=0&&Card(i).Top<=Height).Select(p=>p.FilePath));foreach(var key in thumbs.Keys.Where(k=>!visible.Contains(k)).ToList()){thumbs[key].Dispose();thumbs.Remove(key);}}
            using (var font = Ui.Font(10))
            for(int i=0;i<Photos.Count;i++)
            {
                var p=Photos[i]; var r=Card(i); if(r.Bottom<0 || r.Top>Height)continue;
                using(var fill=new SolidBrush(p.Copies>0&&!SelectionList?Color.FromArgb(130,177,244):Color.FromArgb(225,226,227))) e.Graphics.FillRectangle(fill,r);
                using(var pen=new Pen(p==FocusPhoto?Color.RoyalBlue:Color.Silver,p==FocusPhoto?2:1)) e.Graphics.DrawRectangle(pen,r.X,r.Y,r.Width,r.Height);
                var imageBox=new Rectangle(r.X+5,r.Y+5,r.Width-10,r.Height-29);
                e.Graphics.FillRectangle(Brushes.White,imageBox);
                Bitmap image; if(!thumbs.TryGetValue(p.FilePath,out image)) { try { image=PhotoEngine.CreateThumbnail(p,180,180); thumbs[p.FilePath]=image; } catch{} }
                if(image!=null) { float scale=Math.Min((float)imageBox.Width/image.Width,(float)imageBox.Height/image.Height); var dest=new RectangleF(imageBox.X+(imageBox.Width-image.Width*scale)/2,imageBox.Y+(imageBox.Height-image.Height*scale)/2,image.Width*scale,image.Height*scale); e.Graphics.DrawImage(image,dest); }
                TextRenderer.DrawText(e.Graphics,p.FileName,font,new Rectangle(r.X+4,r.Bottom-20,r.Width-30,17),Color.Black,TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(e.Graphics,"x "+p.Copies,font,new Rectangle(r.Right-30,r.Bottom-20,28,17),Color.Black,TextFormatFlags.Right);
                if(p==FocusPhoto&&!SelectionList)
                {
                    e.Graphics.FillRectangle(Brushes.White,r.X+5,r.Bottom-25,r.Width-10,21);
                    using(var brush=new SolidBrush(Color.FromArgb(62,76,84))) { e.Graphics.FillRectangle(brush,r.X+7,r.Bottom-23,31,17); e.Graphics.FillRectangle(brush,r.Right-38,r.Bottom-23,31,17); }
                    using(var symbolFont=Ui.Font(16)){TextRenderer.DrawText(e.Graphics,"−",symbolFont,new Rectangle(r.X+7,r.Bottom-25,31,20),Color.White,TextFormatFlags.HorizontalCenter);
                    TextRenderer.DrawText(e.Graphics,"+",symbolFont,new Rectangle(r.Right-38,r.Bottom-25,31,20),Color.White,TextFormatFlags.HorizontalCenter);}
                    TextRenderer.DrawText(e.Graphics,p.Copies.ToString(),font,new Rectangle(r.X+39,r.Bottom-23,r.Width-78,18),Color.Black,TextFormatFlags.HorizontalCenter);
                }
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            for(int i=0;i<Photos.Count;i++) { var r=Card(i); if(!r.Contains(e.Location))continue; var p=Photos[i]; bool wasFocused=p==FocusPhoto; FocusPhoto=p; if(PhotoFocused!=null)PhotoFocused(p);
                if(e.Button==MouseButtons.Left && !SelectionList) { if(wasFocused&&e.Y>=r.Bottom-25) { if(e.X<=r.X+38)p.Copies=Math.Max(0,p.Copies-1); else if(e.X>=r.Right-38)p.Copies=Math.Min(99,p.Copies+1); } else if(p.Copies==0&&OneClickSelection)p.Copies=1; if(CopiesChanged!=null)CopiesChanged(p); }
                if(e.Button==MouseButtons.Left&&(SelectionList||AllowPhotoDrag)){dragPhoto=p;dragStart=e.Location;}
                Invalidate(); break;
            }
        }
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragPhoto==null||e.Button!=MouseButtons.Left||Math.Abs(e.X-dragStart.X)+Math.Abs(e.Y-dragStart.Y)<6)return;var photo=dragPhoto;dragPhoto=null;DoDragDrop(photo.FilePath,DragDropEffects.Copy);}
        protected override void OnMouseUp(MouseEventArgs e){dragPhoto=null;base.OnMouseUp(e);}
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); if(FocusPhoto!=null&&PhotoEnlarged!=null)PhotoEnlarged(FocusPhoto); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if(FocusPhoto==null)return; if(e.KeyCode==Keys.Add||e.KeyCode==Keys.Oemplus)FocusPhoto.Copies=Math.Min(99,FocusPhoto.Copies+1); else if(e.KeyCode==Keys.Subtract||e.KeyCode==Keys.OemMinus)FocusPhoto.Copies=Math.Max(0,FocusPhoto.Copies-1); else if(e.KeyCode==Keys.Enter&&PhotoEnlarged!=null)PhotoEnlarged(FocusPhoto); if(CopiesChanged!=null)CopiesChanged(FocusPhoto); Invalidate(); }
        protected override void Dispose(bool disposing) { if(disposing)foreach(var image in thumbs.Values)image.Dispose(); base.Dispose(disposing); }
    }
    public class PagePreview : ScrollableControl
    {
        public PhotoLayout LayoutDefinition;
        public PhotoPage Page;
        public PrintOptions Options;
        public PhotoItem FocusPhoto;
        public bool Corrected;
        public bool EnableManual;
        bool fitToWindow=true;
        int pressedSlot=-1;
        Point pressedPoint;
        bool dragCancelled;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool FitToWindow{get{return fitToWindow;}set{fitToWindow=value;RefreshScale();}}
        public event Action<PhotoItem> PhotoFocused;
        public event Action<int,string> PhotoDropped;
        public event Action<int,int> PhotoMoved;
        public event Action<int> SlotCleared;
        public PagePreview() { DoubleBuffered=true; BackColor=Color.FromArgb(235,236,238); AllowDrop=true;AutoScroll=true; }
        public void RefreshScale(){AutoScrollMinSize=LayoutDefinition==null||fitToWindow?Size.Empty:new Size((int)(LayoutDefinition.Width*96/360)+24,(int)(LayoutDefinition.Height*96/360)+22);Invalidate();}
        public RectangleF Sheet { get { if(LayoutDefinition==null)return RectangleF.Empty;if(fitToWindow)return FrameSpool.LayoutEngine.PaperBounds(new RectangleF(12,10,Math.Max(1,ClientSize.Width-24),Math.Max(1,ClientSize.Height-22)),LayoutDefinition);float w=LayoutDefinition.Width*96/360,h=LayoutDefinition.Height*96/360;return new RectangleF(Math.Max(12,(ClientSize.Width-w)/2)+AutoScrollPosition.X,Math.Max(10,(ClientSize.Height-h)/2)+AutoScrollPosition.Y,w,h); } }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); if(LayoutDefinition==null)return; var sheet=Sheet; e.Graphics.FillRectangle(Brushes.Gray,sheet.X+3,sheet.Y+3,sheet.Width,sheet.Height); FrameSpool.LayoutEngine.DrawPage(e.Graphics,sheet,LayoutDefinition,Page,Options??new PrintOptions(),Corrected,FocusPhoto); }
        int At(Point point) { var sheet=Sheet; if(LayoutDefinition==null)return -1; for(int i=0;i<LayoutDefinition.Frames.Count;i++){var f=LayoutDefinition.Frames[i];if(new RectangleF(sheet.X+f.X*sheet.Width,sheet.Y+f.Y*sheet.Height,f.Width*sheet.Width,f.Height*sheet.Height).Contains(point))return i;}return -1; }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); int i=At(e.Location);pressedSlot=-1;if(Page!=null&&i>=0&&i<Page.Photos.Count&&Page.Photos[i]!=null){FocusPhoto=Page.Photos[i]; if(PhotoFocused!=null)PhotoFocused(FocusPhoto);if(EnableManual&&e.Button==MouseButtons.Left){pressedSlot=i;pressedPoint=e.Location;}if(EnableManual&&e.Button==MouseButtons.Right){var popup=new ContextMenuStrip();popup.Items.Add("Clear Settings",null,delegate{if(SlotCleared!=null)SlotCleared(i);});popup.Closed+=delegate{popup.Dispose();};popup.Show(this,e.Location);} Invalidate();} }
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!EnableManual||pressedSlot<0||e.Button!=MouseButtons.Left)return;if(Math.Abs(e.X-pressedPoint.X)+Math.Abs(e.Y-pressedPoint.Y)<6)return;int slot=pressedSlot;pressedSlot=-1;var data=new DataObject();data.SetData("FrameSpool.Slot",slot);dragCancelled=false;var result=DoDragDrop(data,DragDropEffects.Move);if(result==DragDropEffects.None&&!dragCancelled&&SlotCleared!=null)SlotCleared(slot);}
        protected override void OnQueryContinueDrag(QueryContinueDragEventArgs e){if(e.EscapePressed)dragCancelled=true;base.OnQueryContinueDrag(e);}
        protected override void OnMouseUp(MouseEventArgs e){pressedSlot=-1;base.OnMouseUp(e);}
        protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e);if(!EnableManual)return;if(e.Data.GetDataPresent("FrameSpool.Slot"))e.Effect=DragDropEffects.Move;else if(e.Data.GetDataPresent(DataFormats.Text)||e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy; }
        protected override void OnDragDrop(DragEventArgs e) { base.OnDragDrop(e);if(!EnableManual)return;int i=At(PointToClient(new Point(e.X,e.Y)));if(i<0)return;if(e.Data.GetDataPresent("FrameSpool.Slot")){if(PhotoMoved!=null)PhotoMoved((int)e.Data.GetData("FrameSpool.Slot"),i);e.Effect=DragDropEffects.Move;return;}if(PhotoDropped!=null){string path=e.Data.GetDataPresent(DataFormats.FileDrop)?((string[])e.Data.GetData(DataFormats.FileDrop))[0]:(string)e.Data.GetData(DataFormats.Text);PhotoDropped(i,path);} }
    }
}
