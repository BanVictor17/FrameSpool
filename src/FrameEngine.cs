using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FrameSpool
{
    // New, editable format. It is not the proprietary P.I.F. file format.
    public sealed class FrameElement
    {
        public string Kind="Opening",Text="",ImagePath="";
        public string CaptionRole="",CaptionValue="",FontFamily="Arial",TextAlignment="Left",TextVerticalAlignment="Top";
        public int CaptionPhotoIndex;
        public bool VerticalText;
        public bool ReverseLine;
        public string ImageDirection="0",ImageGroup="";
        public int ImageRotation;
        public float X=.1f,Y=.1f,Width=.8f,Height=.8f,Rotation,Points=18,Thickness=2;
        public int ColorArgb=Color.Black.ToArgb();
        public RectangleF Bounds{get{return new RectangleF(X,Y,Width,Height);}}
        public override string ToString(){return Kind=="Text"?"Text: "+Text:Kind;}
    }
    public sealed class FrameDesign
    {
        public int Version=1;
        public string Id=Guid.NewGuid().ToString("N"),Name="New Frame",Paper="A4",Category="Other";
        public float Width=2976,Height=4209;
        public int BackingColor=Color.White.ToArgb();
        public List<FrameElement> Elements=new List<FrameElement>();
        public override string ToString(){return Name;}
        public FrameDesign Clone(){return new JavaScriptSerializer().Deserialize<FrameDesign>(new JavaScriptSerializer().Serialize(this));}
        public PhotoLayout ToLayout()
        {
            var layout=new PhotoLayout{Id="custom:"+Id,Name=Name,Paper=Paper,Width=Width,Height=Height,Design=this};
            foreach(var element in Elements.Where(IsOpening))
            {layout.Frames.Add(element.Bounds);layout.Images.Add(new LayoutImage{Direction=element.ImageDirection,Group=element.ImageGroup,Rotation=element.ImageRotation});}
            layout.Info=Elements.Any(e=>e.CaptionRole=="info"||e.CaptionRole=="exif");layout.Comments=Elements.Any(e=>e.CaptionRole=="comment");
            return layout;
        }
        public static bool IsOpening(FrameElement element){return element.Kind=="Opening"||element.Kind=="Ellipse Opening";}
        public void Draw(Graphics graphics,RectangleF page,PhotoPage content,bool corrected,PhotoItem focus,bool editor,PrintOptions options=null)
        {
            using(var brush=new SolidBrush(Color.FromArgb(BackingColor)))graphics.FillRectangle(brush,page);
            int position=0;
            foreach(var item in Elements)
            {
                var box=new RectangleF(page.X+item.X*page.Width,page.Y+item.Y*page.Height,item.Width*page.Width,item.Height*page.Height);
                var state=graphics.Save();
                try
                {
                    if(item.Rotation!=0){graphics.TranslateTransform(box.X+box.Width/2,box.Y+box.Height/2);graphics.RotateTransform(item.Rotation);graphics.TranslateTransform(-box.X-box.Width/2,-box.Y-box.Height/2);}
                    if(IsOpening(item))
                    {
                        var photo=content!=null&&position<content.Photos.Count?content.Photos[position]:null;
                        using(var path=new GraphicsPath())
                        {
                            if(item.Kind=="Ellipse Opening")path.AddEllipse(box);else path.AddRectangle(box);
                            graphics.SetClip(path,CombineMode.Intersect);
                            if(photo!=null)
                            {
                                int imageRotation=item.ImageRotation;
                                if(item.ImageDirection=="180")imageRotation+=2;
                                if((item.ImageDirection=="AutoLeft"||item.ImageDirection=="Auto")&&(photo.Width>=photo.Height)!=(box.Width>=box.Height))imageRotation+=3;
                                PhotoEngine.DrawPhoto(graphics,photo,box,corrected,imageRotation,page.Width/Width,page.Height/Height);
                            }
                            else if(editor)graphics.FillRectangle(Brushes.LightSteelBlue,box);
                            graphics.ResetClip();
                            if(editor||photo==focus&&photo!=null)using(var pen=new Pen(photo==focus?Color.RoyalBlue:Color.SlateGray,1))graphics.DrawPath(pen,path);
                        }
                        position++;
                    }
                    else if(item.Kind=="Decoration"&&File.Exists(item.ImagePath))
                    {using(var image=Image.FromFile(item.ImagePath))using(var attributes=new ImageAttributes()){attributes.SetWrapMode(WrapMode.TileFlipXY);graphics.DrawImage(image,new[]{new PointF(box.Left,box.Top),new PointF(box.Right,box.Top),new PointF(box.Left,box.Bottom)},new RectangleF(0,0,image.Width,image.Height),GraphicsUnit.Pixel,attributes);}}
                    else if(item.Kind=="Text")
                    {
                        string caption=item.Text;
                        if(!editor&&!String.IsNullOrEmpty(item.CaptionRole))
                        {
                            if(options==null)options=new PrintOptions();
                            if((item.CaptionRole=="info"||item.CaptionRole=="exif")&&!options.PrintInfo)continue;
                            var photo=content!=null&&item.CaptionPhotoIndex>=0&&item.CaptionPhotoIndex<content.Photos.Count?content.Photos[item.CaptionPhotoIndex]:null;
                            caption=LayoutEngine.CaptionText(item.CaptionRole,item.CaptionValue,photo,options);
                        }
                        LayoutEngine.DrawCaption(graphics,box,caption,new LayoutCaption{Family=item.FontFamily,Points=item.Points,Color=Color.FromArgb(item.ColorArgb),Alignment=item.TextAlignment,VerticalAlignment=item.TextVerticalAlignment,Vertical=item.VerticalText},page.Width/Width);
                    }
                    else using(var pen=new Pen(Color.FromArgb(item.ColorArgb),Math.Max(.5f,item.Thickness*page.Width/Width)))
                    {if(item.Kind=="Line")graphics.DrawLine(pen,box.Left,item.ReverseLine?box.Bottom:box.Top,box.Right,item.ReverseLine?box.Top:box.Bottom);else if(item.Kind=="Box")graphics.DrawRectangle(pen,box.X,box.Y,box.Width,box.Height);}
                }
                finally{graphics.Restore(state);}
            }
        }
    }
    public static class FrameStore
    {
        public static readonly string Folder=Path.Combine(AppIdentity.DataDirectory,"frames");
        public static List<FrameDesign> Load()
        {
            var designs=new List<FrameDesign>();if(!Directory.Exists(Folder))return designs;
            foreach(string file in Directory.GetFiles(Folder,"*.eppframe.json"))try{designs.Add(Read(file));}catch{}
            return designs;
        }
        public static FrameDesign Read(string path)
        {
            if(new FileInfo(path).Length>2000000)throw new InvalidDataException("The frame definition is too large.");
            var frame=new JavaScriptSerializer().Deserialize<FrameDesign>(File.ReadAllText(path));Validate(frame);
            string sourceFolder=Path.GetDirectoryName(Path.GetFullPath(path))+Path.DirectorySeparatorChar,libraryFolder=Path.GetFullPath(Folder)+Path.DirectorySeparatorChar;
            foreach(var item in frame.Elements.Where(e=>e.Kind=="Decoration"&&!String.IsNullOrEmpty(e.ImagePath)))
            {string image=Path.GetFullPath(Path.IsPathRooted(item.ImagePath)?item.ImagePath:Path.Combine(sourceFolder,item.ImagePath));if(!image.StartsWith(sourceFolder,StringComparison.OrdinalIgnoreCase)&&!image.StartsWith(libraryFolder,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Decoration images must be stored beside the frame definition.");item.ImagePath=image;}
            return frame;
        }
        public static void Validate(FrameDesign frame)
        {
            if(frame==null||frame.Version!=1||!Finite(frame.Width)||!Finite(frame.Height)||frame.Width<=0||frame.Height<=0||frame.Width>30000||frame.Height>30000)throw new InvalidDataException("Invalid frame size or format.");
            if(frame.Elements==null||frame.Elements.Count>1000||!frame.Elements.Any(FrameDesign.IsOpening))throw new InvalidDataException("A frame needs at least one photo opening and can contain up to 1000 items.");
            foreach(var item in frame.Elements)if(item==null||!new[]{"Opening","Ellipse Opening","Decoration","Text","Line","Box"}.Contains(item.Kind)||!Finite(item.X)||!Finite(item.Y)||Math.Abs(item.X)>2||Math.Abs(item.Y)>2||!Finite(item.Width)||!Finite(item.Height)||item.Width<=0||item.Height<=0||item.Width>4||item.Height>4||!Finite(item.Rotation)||!Finite(item.Points)||!Finite(item.Thickness)||item.Points<=0||item.Points>200||item.Thickness<0||item.Thickness>100)throw new InvalidDataException("Invalid frame item.");
            Guid id;if(!Guid.TryParse(frame.Id,out id))frame.Id=Guid.NewGuid().ToString("N");
            if(String.IsNullOrWhiteSpace(frame.Name))frame.Name="New Frame";
        }
        static bool Finite(float value){return !Single.IsNaN(value)&&!Single.IsInfinity(value);}
        public static void Save(FrameDesign frame)
        {
            Validate(frame);Directory.CreateDirectory(Folder);
            var imageFolder=Path.Combine(Folder,frame.Id);
            foreach(var item in frame.Elements.Where(e=>e.Kind=="Decoration"&&File.Exists(e.ImagePath)))
            {
                Directory.CreateDirectory(imageFolder);if(String.Equals(Path.GetDirectoryName(Path.GetFullPath(item.ImagePath)),Path.GetFullPath(imageFolder),StringComparison.OrdinalIgnoreCase))continue;string target=Path.Combine(imageFolder,Guid.NewGuid().ToString("N")+".png");
                using(var image=Image.FromFile(item.ImagePath))image.Save(target,ImageFormat.Png);
                item.ImagePath=target;
            }
            File.WriteAllText(Path.Combine(Folder,frame.Id+".eppframe.json"),new JavaScriptSerializer().Serialize(frame));
        }
        public static void Remove(FrameDesign frame){string file=Path.Combine(Folder,frame.Id+".eppframe.json");if(File.Exists(file))File.Delete(file);}
    }
    public sealed class FrameCanvas : Control
    {
        public FrameDesign Design;
        public FrameElement Selected;
        public event Action<FrameElement> SelectionChanged;
        public event Action BeforeMove,AfterMove;
        PointF startingPoint;float startX,startY;bool dragging;
        public FrameCanvas(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);BackColor=Color.FromArgb(225,227,232);}
        public RectangleF Sheet{get{return Design==null?RectangleF.Empty:FrameSpool.LayoutEngine.PaperBounds(new RectangleF(15,15,Math.Max(1,Width-30),Math.Max(1,Height-30)),Design.ToLayout());}}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Design==null)return;var page=Sheet;Design.Draw(e.Graphics,page,null,false,null,true);if(Selected!=null){var r=Selected.Bounds;using(var pen=new Pen(Color.RoyalBlue,2){DashStyle=DashStyle.Dash})e.Graphics.DrawRectangle(pen,page.X+r.X*page.Width,page.Y+r.Y*page.Height,r.Width*page.Width,r.Height*page.Height);}}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);var page=Sheet;if(page.IsEmpty)return;startingPoint=new PointF((e.X-page.X)/page.Width,(e.Y-page.Y)/page.Height);Selected=Design.Elements.LastOrDefault(item=>item.Bounds.Contains(startingPoint));if(SelectionChanged!=null)SelectionChanged(Selected);if(Selected!=null&&e.Button==MouseButtons.Left){startX=Selected.X;startY=Selected.Y;dragging=true;Capture=true;if(BeforeMove!=null)BeforeMove();}Invalidate();}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!dragging||Selected==null||e.Button!=MouseButtons.Left)return;var page=Sheet;if(page.IsEmpty)return;Selected.X=Math.Max(0,Math.Min(1-Selected.Width,(e.X-page.X)/page.Width-startingPoint.X+startX));Selected.Y=Math.Max(0,Math.Min(1-Selected.Height,(e.Y-page.Y)/page.Height-startingPoint.Y+startY));Invalidate();}
        void EndMove(){if(!dragging)return;dragging=false;Capture=false;if(AfterMove!=null)AfterMove();}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);EndMove();}
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)EndMove();}
        protected override void OnResize(EventArgs e){base.OnResize(e);EndMove();Invalidate();}
    }
    public sealed class FrameEditor : DpiForm
    {
        public FrameDesign Design;
        FrameCanvas canvas;ListBox objects;TextBox name,text;
        NumericUpDown x,y,width,height,rotation,points,thickness;
        FrameDesign undo;bool syncing;
        public FrameEditor(FrameDesign design)
        {
            Design=design.Clone();Text="Edit Frame";ClientSize=new Size(840,600);MinimumSize=Size;Font=Ui.Font(12);AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.CenterParent;BackColor=Color.FromArgb(252,252,254);
            Ui.Label(this,"Frame Name",12,12,85,20);name=new TextBox{Bounds=new Rectangle(97,9,320,24),Text=Design.Name};Controls.Add(name);
            Ui.Label(this,"Category",424,12,63,20);Ui.Combo(this,new[]{"PhotoFrame","Greeting","Calender","Album","Other"},487,9,130,Design.Category,delegate(string category){Design.Category=category;});
            Ui.Button(this,"Backing Paper",12,43,110,28,delegate{using(var colors=new ColorDialog{Color=Color.FromArgb(Design.BackingColor)})if(colors.ShowDialog(this)==DialogResult.OK){Remember();Design.BackingColor=colors.Color.ToArgb();RefreshDesign();}});
            Ui.Button(this,"Picture Frame",127,43,110,28,delegate{Add("Opening");});Ui.Button(this,"Oval",242,43,55,28,delegate{Add("Ellipse Opening");});
            Ui.Button(this,"Decoration",302,43,105,28,delegate{using(var dialog=new OpenFileDialog{Filter="Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif"})if(dialog.ShowDialog(this)==DialogResult.OK){Add("Decoration");canvas.Selected.ImagePath=dialog.FileName;RefreshDesign();}});
            Ui.Button(this,"Text",412,43,65,28,delegate{Add("Text");});Ui.Button(this,"Line",482,43,65,28,delegate{Add("Line");});Ui.Button(this,"Box",552,43,65,28,delegate{Add("Box");});
            canvas=new FrameCanvas{Bounds=new Rectangle(12,82,605,466),Design=Design};Controls.Add(canvas);
            objects=new ListBox{Bounds=new Rectangle(632,12,195,180),IntegralHeight=false};Controls.Add(objects);objects.SelectedIndexChanged+=delegate{if(syncing)return;canvas.Selected=objects.SelectedItem as FrameElement;Sync();canvas.Invalidate();};
            canvas.SelectionChanged+=delegate{Sync();};canvas.BeforeMove+=Remember;canvas.AfterMove+=Sync;
            x=Property("Left (%)",210,0,100,10);y=Property("Top (%)",240,0,100,10);width=Property("Width (%)",270,1,100,80);height=Property("Height (%)",300,1,100,80);
            rotation=Property("Rotation",330,-360,360,0);points=Property("Text Size (pt)",360,1,200,18);thickness=Property("Line Width",390,0,100,2);
            text=new TextBox{Bounds=new Rectangle(632,425,195,48),Multiline=true};Controls.Add(text);text.TextChanged+=delegate{if(syncing||canvas.Selected==null)return;Remember();canvas.Selected.Text=text.Text;canvas.Invalidate();};
            Ui.Button(this,"Color",632,481,92,25,delegate{if(canvas.Selected==null)return;using(var colors=new ColorDialog{Color=Color.FromArgb(canvas.Selected.ColorArgb)})if(colors.ShowDialog(this)==DialogResult.OK){Remember();canvas.Selected.ColorArgb=colors.Color.ToArgb();canvas.Invalidate();}});
            Ui.Button(this,"Delete",735,481,92,25,delegate{if(canvas.Selected!=null){Remember();Design.Elements.Remove(canvas.Selected);canvas.Selected=null;RefreshDesign();}});
            Ui.Button(this,"Move Up",632,518,92,25,delegate{Reorder(1);});Ui.Button(this,"Move Down",735,518,92,25,delegate{Reorder(-1);});
            Ui.Button(this,"Undo",12,566,85,25,delegate{if(undo!=null){Design=undo;undo=null;canvas.Design=Design;canvas.Selected=null;RefreshDesign();}});
            Ui.Button(this,"Save",635,566,90,25,delegate{Design.Name=name.Text;try{FrameStore.Validate(Design);DialogResult=DialogResult.OK;}catch(Exception error){MessageBox.Show(this,error.Message,"Frame");}});
            var cancel=Ui.Button(this,"Cancel",737,566,90,25,delegate{DialogResult=DialogResult.Cancel;});CancelButton=cancel;RefreshDesign();
        }
        NumericUpDown Property(string label,int top,int min,int max,int value){Ui.Label(this,label,632,top+3,100,20);var input=Ui.Number(this,737,top,90,min,max,value,null);input.DecimalPlaces=1;input.ValueChanged+=delegate{if(syncing||canvas.Selected==null)return;Remember();var item=canvas.Selected;item.X=(float)x.Value/100;item.Y=(float)y.Value/100;item.Width=(float)width.Value/100;item.Height=(float)height.Value/100;item.Rotation=(float)rotation.Value;item.Points=(float)points.Value;item.Thickness=(float)thickness.Value;canvas.Invalidate();};return input;}
        void Remember(){if(!syncing)undo=Design.Clone();}
        void Add(string kind){Remember();var item=new FrameElement{Kind=kind,X=.1f,Y=.1f,Width=kind=="Text"?.7f:.6f,Height=kind=="Text"?.15f:kind=="Line"?.1f:.6f,Text=kind=="Text"?"Your text":""};Design.Elements.Add(item);canvas.Selected=item;RefreshDesign();}
        void Reorder(int delta){int index=Design.Elements.IndexOf(canvas.Selected),next=index+delta;if(index<0||next<0||next>=Design.Elements.Count)return;Remember();var item=Design.Elements[index];Design.Elements.RemoveAt(index);Design.Elements.Insert(next,item);RefreshDesign();}
        void RefreshDesign(){syncing=true;objects.Items.Clear();foreach(var item in Design.Elements)objects.Items.Add(item);objects.SelectedItem=canvas.Selected;syncing=false;Sync();canvas.Invalidate();}
        void Sync(){syncing=true;objects.SelectedItem=canvas.Selected;var item=canvas.Selected;foreach(var input in new[]{x,y,width,height,rotation,points,thickness})input.Enabled=item!=null;text.Enabled=item!=null&&item.Kind=="Text";if(item!=null){Set(x,item.X*100);Set(y,item.Y*100);Set(width,item.Width*100);Set(height,item.Height*100);Set(rotation,item.Rotation);Set(points,item.Points);Set(thickness,item.Thickness);text.Text=item.Text;}else text.Text="";syncing=false;}
        static void Set(NumericUpDown input,float value){input.Value=Math.Max(input.Minimum,Math.Min(input.Maximum,(decimal)value));}
    }
}
