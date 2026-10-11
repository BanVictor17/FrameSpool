using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FrameSpool
{
    public class CorrectionCanvas : Control
    {
        public Bitmap ImageBitmap;
        public bool Fit = true;
        public event Action<PointF> ImageClicked;
        public CorrectionCanvas(){DoubleBuffered=true;BackColor=Color.FromArgb(234,237,241);}
        public RectangleF ImageRectangle
        {
            get { if(ImageBitmap==null)return RectangleF.Empty;float s=Fit?Math.Min((Width-12f)/ImageBitmap.Width,(Height-12f)/ImageBitmap.Height):1;return new RectangleF((Width-ImageBitmap.Width*s)/2,(Height-ImageBitmap.Height*s)/2,ImageBitmap.Width*s,ImageBitmap.Height*s); }
        }
        public void SetImage(Bitmap bitmap){var previous=ImageBitmap;ImageBitmap=bitmap;if(previous!=null)previous.Dispose();Invalidate();}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(ImageBitmap!=null){e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;var r=ImageRectangle;e.Graphics.DrawImage(ImageBitmap,r,new RectangleF(0,0,ImageBitmap.Width,ImageBitmap.Height),GraphicsUnit.Pixel);}}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);var r=ImageRectangle;if(r.Contains(e.Location)&&ImageClicked!=null)ImageClicked(new PointF((e.X-r.X)/r.Width,(e.Y-r.Y)/r.Height));}
        protected override void Dispose(bool disposing){if(disposing&&ImageBitmap!=null)ImageBitmap.Dispose();base.Dispose(disposing);}
    }
    public partial class MainForm
    {
        void ShowCorrection()
        {
            if(FocusPhoto==null)return;
            using(var f=Ui.Dialog("Print Correction",792,566))
            {
                var photo=FocusPhoto;int viewingRotation=0;bool loading=true;
                f.BackgroundImage=Skin.GetImage("Image/bg_hosei.bmp");f.BackgroundImageLayout=ImageLayout.Stretch;
                var message=Ui.Label(f,"Make image correction settings that take effect during printing.\r\nTo print comments, select a layout with one or more frames for comments.\r\nTo fix the photo manually, click up to 20 red-eye parts in the image.",16,15,510,48);
                var canvas=new CorrectionCanvas{Bounds=new Rectangle(12,70,505,390)};f.Controls.Add(canvas);
                var file=Ui.Label(f,"",25,513,350,15);var path=Ui.Label(f,"",25,528,350,15);
                Ui.Label(f,"Comments:",25,491,90,16);var comments=new TextBox{Bounds=new Rectangle(120,484,220,24),MaxLength=60,Font=Ui.Font(12),Enabled=CurrentLayout!=null&&CurrentLayout.Comments};f.Controls.Add(comments);
                Ui.Button(f,"Apply to All Photos",347,486,170,21,delegate{foreach(var p in Selected)p.Comment=comments.Text;});
                var fit=Ui.Check(f,"Fit to Window",537,12,144,true,delegate(bool value){canvas.Fit=value;canvas.Invalidate();});Ui.Button(f,"Help",684,19,87,21,delegate{ShowHelp("Print Correction");});
                var right=new Panel{Bounds=new Rectangle(533,58,249,367),BackColor=Color.FromArgb(233,236,240)};f.Controls.Add(right);
                Ui.Label(right,"Auto Correct",18,3,211,16);
                var fix=new RadioButton{Text="Fix Photo",Bounds=new Rectangle(23,20,206,19),Font=Ui.Font(12)};var pim=new RadioButton{Text="PRINT Image Matching",Bounds=new Rectangle(23,40,206,19),Font=Ui.Font(12),Enabled=false};var none=new RadioButton{Text="None",Bounds=new Rectangle(23,60,206,19),Font=Ui.Font(12)};right.Controls.Add(fix);right.Controls.Add(pim);right.Controls.Add(none);
                Ui.Label(right,"Scene Correction",18,100,126,16);var scene=Ui.Combo(right,new[]{"Standard","People","Landscape","Night Scene"},144,99,100,"Standard",null);
                Ui.Label(right,"Scene Correction Level",24,122,210,16);var level=Slider(right,59,140,137,-100,100,0);
                Ui.Label(right,"−",41,151,14,16);Ui.Label(right,"+",199,151,14,16);
                var mono=Ui.Check(right,"Monochrome",23,183,216,false,null);var vivid=Ui.Check(right,"Vivid && Clear",23,203,216,false,null);
                Ui.Label(right,"Brightness",24,223,200,16);var brightness=Slider(right,59,240,137,-100,100,0);Ui.Label(right,"−",41,251,14,16);Ui.Label(right,"+",199,251,14,16);
                var noise=Ui.Check(right,"Digital Camera Correction",23,282,225,false,null);var standard=new RadioButton{Text="Standard",Bounds=new Rectangle(43,302,88,20),Checked=true,Font=Ui.Font(12)};var smooth=new RadioButton{Text="Smooth Skin",Bounds=new Rectangle(136,302,110,20),Font=Ui.Font(12)};
                // Put the noise radio group in its own container to keep Auto Correct independent.
                var noiseChoices=new Panel{Bounds=new Rectangle(22,302,223,20)};standard.Location=new Point(20,0);smooth.Location=new Point(112,0);noiseChoices.Controls.Add(standard);noiseChoices.Controls.Add(smooth);right.Controls.Add(noiseChoices);
                var autoRed=Ui.Check(right,"Fix Red-Eye (Auto)",23,323,216,false,null);
                var manual=Ui.Check(f,"Fix Red-Eye (Manual)",555,435,225,false,null);
                Action render=null;Action sync=null;Action store=null;
                var rawButton=Ui.Button(f,"RAW Development...",12,464,153,21,delegate{ShowRawDevelopment(photo);render();});rawButton.Enabled=photo.IsRaw;
                Ui.Button(f,"Histogram...",173,464,153,21,delegate{ShowHistogram(photo);});
                render=delegate{try{var view=photo.Clone();view.Rotation=photo.Rotation+viewingRotation;canvas.SetImage(PhotoEngine.RenderImage(view,true));file.Text="File:       "+photo.FileName;path.Text="Location: "+Path.GetDirectoryName(photo.FilePath);}catch(Exception ex){MessageBox.Show(f,ex.Message,"Image Correct");}};
                store=delegate{if(loading)return;var a=photo.Adjustments;a.Mode=none.Checked?"None":pim.Checked?"PIM":"PhotoEnhance";a.Scene=scene.Text;a.SceneLevel=level.Value;a.Brightness=brightness.Value;a.Monochrome=mono.Checked;a.Vivid=vivid.Checked;a.DigitalCorrection=noise.Checked;a.SmoothSkin=smooth.Checked;a.AutoRedEye=autoRed.Checked;mono.Enabled=scene.Text!="Night Scene";if(!mono.Enabled)a.Monochrome=false;scene.Enabled=level.Enabled=fix.Checked;standard.Enabled=smooth.Enabled=noise.Checked;render();};
                sync=delegate{loading=true;rawButton.Enabled=photo.IsRaw;var a=photo.Adjustments;fix.Checked=a.Mode!="None"&&a.Mode!="PIM";none.Checked=a.Mode=="None";pim.Checked=a.Mode=="PIM";scene.SelectedItem=a.Scene;level.Value=Math.Max(-100,Math.Min(100,a.SceneLevel));brightness.Value=Math.Max(-100,Math.Min(100,a.Brightness));mono.Checked=a.Monochrome;vivid.Checked=a.Vivid;noise.Checked=a.DigitalCorrection;smooth.Checked=a.SmoothSkin;standard.Checked=!a.SmoothSkin;standard.Enabled=smooth.Enabled=noise.Checked;scene.Enabled=level.Enabled=fix.Checked;autoRed.Checked=a.AutoRedEye;comments.Text=photo.Comment;autoRed.Enabled=PhotoEngine.CanCorrectRedEye(photo);loading=false;render();};
                fix.CheckedChanged+=delegate{store();};none.CheckedChanged+=delegate{store();};pim.CheckedChanged+=delegate{store();};scene.SelectedIndexChanged+=delegate{store();};level.ValueChanged+=delegate{store();};brightness.ValueChanged+=delegate{store();};mono.CheckedChanged+=delegate{store();};vivid.CheckedChanged+=delegate{store();};noise.CheckedChanged+=delegate{store();};autoRed.CheckedChanged+=delegate{store();};comments.TextChanged+=delegate{if(!loading)photo.Comment=comments.Text;};
                standard.CheckedChanged+=delegate{store();};smooth.CheckedChanged+=delegate{store();};
                Ui.Button(right,"Apply to All Photos",59,343,179,21,delegate{foreach(var p in Selected){var points=p.Adjustments.ManualEyePoints; p.Adjustments=photo.Adjustments.Clone();p.Adjustments.ManualEyePoints=points;}UpdatePage();});
                Ui.Skin(f,"","Image/btn_2-4-02_u.bmp",537,33,32,20,delegate{viewingRotation=(viewingRotation+3)%4;render();});Ui.Skin(f,"","Image/btn_2-4-03_u.bmp",577,33,32,20,delegate{viewingRotation=(viewingRotation+1)%4;render();});
                manual.CheckedChanged+=delegate{canvas.Cursor=manual.Checked?Cursors.Cross:Cursors.Default;};
                canvas.ImageClicked+=delegate(PointF point){if(!manual.Checked)return;var rotated=PhotoEngine.ToOriginalEyePoint(point,photo.Rotation+viewingRotation);if(PhotoEngine.AddManualEye(photo,rotated))render();else MessageBox.Show(f,"You can specify up to 20 red-eye positions on an image with 20 million pixels or less.","Fix Red-Eye");};
                Ui.Button(f,"Undo",543,457,110,21,delegate{var points=photo.Adjustments.ManualEyePoints;if(points.Count>0){points.RemoveAt(points.Count-1);render();}});Ui.Button(f,"Clear",662,457,110,21,delegate{photo.Adjustments.ManualEyePoints.Clear();render();});
                Ui.Button(f,"Portrait Enhance",538,524,137,21,delegate{ShowPortrait(photo);render();});Ui.Button(f,"Close",684,524,87,21,delegate{f.Close();});
                Ui.Button(f,"Previous",337,524,85,21,delegate{var all=Selected;int n=all.IndexOf(photo.Original??photo);if(n>0){photo=all[n-1];viewingRotation=0;Focus(photo);sync();}});Ui.Button(f,"Next",432,524,85,21,delegate{var all=Selected;int n=all.IndexOf(photo.Original??photo);if(n<all.Count-1){photo=all[n+1];viewingRotation=0;Focus(photo);sync();}});
                sync();f.ShowDialog(this);UpdatePage();
            }
        }
        static TrackBar Slider(Control parent,int x,int y,int width,int min,int max,int value)
        {var t=new TrackBar{Bounds=new Rectangle(x,y,width,40),Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,value)),TickStyle=TickStyle.None,AutoSize=false,SmallChange=1,LargeChange=5};parent.Controls.Add(t);return t;}
        void ShowHistogram(PhotoItem photo)
        {
            using(var dialog=Ui.Dialog("RGB Histogram",500,260))using(var image=PhotoEngine.CreateHistogram(photo,470,190))
            {dialog.Controls.Add(new PictureBox{Bounds=new Rectangle(15,15,470,190),Image=image});Ui.Button(dialog,"Close",395,222,90,23,delegate{dialog.Close();});dialog.ShowDialog(this);}
        }
        void ShowRawDevelopment(PhotoItem photo)
        {
            if(!photo.IsRaw)return;
            using(var dialog=Ui.Dialog("RAW Development",520,412))
            {
                var previewImage=new CorrectionCanvas{Bounds=new Rectangle(15,12,490,250)};dialog.Controls.Add(previewImage);
                Ui.Label(dialog,"Exposure (EV)",15,281,135,22);
                var exposure=new NumericUpDown{Bounds=new Rectangle(158,281,95,24),Minimum=-3,Maximum=3,Increment=.5m,DecimalPlaces=1,Value=Math.Max(-3,Math.Min(3,photo.Adjustments.RawExposureSteps*.5m))};dialog.Controls.Add(exposure);
                Ui.Label(dialog,"White Balance",15,315,135,22);
                var balance=Ui.Combo(dialog,new[]{"As Shot","Daylight","Cloudy","Shade","Fluorescent","Incandescent","Flash"},158,315,220,photo.Adjustments.RawWhiteBalance,null);
                balance.Enabled=!photo.Adjustments.Monochrome;
                Ui.Label(dialog,"Camera presets are used when available. Other presets use temperature estimates.",15,346,490,30);
                Action render=delegate{try{photo.Adjustments.RawExposureSteps=(int)(exposure.Value*2);photo.Adjustments.RawWhiteBalance=balance.Text;previewImage.SetImage(PhotoEngine.RenderImage(photo,true));}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"RAW Development");}};
                exposure.ValueChanged+=delegate{render();};balance.SelectedIndexChanged+=delegate{render();};
                Ui.Button(dialog,"Reset",15,381,90,23,delegate{exposure.Value=0;balance.SelectedItem="As Shot";render();});
                Ui.Button(dialog,"Close",415,381,90,23,delegate{dialog.Close();});render();dialog.ShowDialog(this);
            }
        }
        void ShowPortrait(PhotoItem photo)
        {
            using(var f=Ui.Dialog("Portrait Enhance",792,566))
            {
                f.BackgroundImage=Skin.GetImage("Image/bg_hosei.bmp");f.BackgroundImageLayout=ImageLayout.Stretch;
                Ui.Label(f,"Improves the quality of portrait images.\r\nSkin Radiance and Face Definition are effective on frontal faces.\r\nIf a face is too small, corrections may have little effect.",20,20,500,48);
                var original=new CorrectionCanvas{Bounds=new Rectangle(12,90,246,390)};var modified=new CorrectionCanvas{Bounds=new Rectangle(268,90,246,390)};f.Controls.Add(original);f.Controls.Add(modified);Ui.Label(f,"Original Image",16,70,200,16);Ui.Label(f,"Modified Image",270,70,240,16);
                Ui.Label(f,"Image View",551,83,220,16);var single=new RadioButton{Text="Single (Modified Image)",Bounds=new Rectangle(556,111,220,19)};var dual=new RadioButton{Text="Dual (Original and Modified)",Bounds=new Rectangle(556,137,230,19),Checked=true};f.Controls.Add(single);f.Controls.Add(dual);
                Ui.Label(f,"Face Definition",551,190,220,16);var face=Slider(f,592,207,137,0,5,photo.Adjustments.FaceDefinition);Ui.Label(f,"0",577,220,14,16);Ui.Label(f,"5",732,220,14,16);
                Ui.Label(f,"Skin Radiance",551,260,220,16);var skin=Slider(f,592,277,137,0,5,photo.Adjustments.SkinRadiance);Ui.Label(f,"0",577,290,14,16);Ui.Label(f,"5",732,290,14,16);
                Ui.Label(f,"Slimming Correction",551,330,220,16);var slim=Slider(f,592,349,137,0,3,photo.Adjustments.Slimming);
                var direction=Ui.Combo(f,new[]{"Vertical","Horizontal"},592,387,137,photo.Adjustments.SlimmingDirection,null);
                Action render=delegate{photo.Adjustments.FaceDefinition=face.Value;photo.Adjustments.SkinRadiance=skin.Value;photo.Adjustments.Slimming=slim.Value;photo.Adjustments.SlimmingDirection=direction.Text;original.SetImage(PhotoEngine.RenderImage(photo,false));modified.SetImage(PhotoEngine.RenderImage(photo,true));};
                direction.SelectedIndexChanged+=delegate{render();};
                face.ValueChanged+=delegate{render();};skin.ValueChanged+=delegate{render();};slim.ValueChanged+=delegate{render();};
                single.CheckedChanged+=delegate{original.Visible=!single.Checked;modified.Bounds=single.Checked?new Rectangle(12,90,502,390):new Rectangle(268,90,246,390);};
                bool portraitSize=Math.Min(photo.Width,photo.Height)>=240;face.Enabled=portraitSize;skin.Enabled=portraitSize&&!photo.Adjustments.Monochrome;
                Ui.Button(f,"Default",567,414,184,21,delegate{face.Value=skin.Value=slim.Value=0;render();});Ui.Button(f,"Apply to All Photos",567,450,184,21,delegate{foreach(var p in Selected){p.Adjustments.FaceDefinition=face.Value;p.Adjustments.SkinRadiance=skin.Value;}});
                Ui.Check(f,"Fit to Window",533,12,144,true,delegate(bool value){original.Fit=modified.Fit=value;original.Invalidate();modified.Invalidate();});Ui.Button(f,"Help",684,19,87,21,delegate{ShowHelp("Portrait Enhance");});Ui.Label(f,"File: "+photo.FileName+"\r\nLocation: "+Path.GetDirectoryName(photo.FilePath),25,513,490,34);Ui.Button(f,"Close",684,522,87,21,delegate{f.Close();});render();f.ShowDialog(this);
            }
        }
    }
}
