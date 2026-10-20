using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class MainForm
    {
        internal static void VerifyNavigation(string directory,string fixture,List<VerificationCheck> checks)
        {
            Action<string,bool> check=(name,passed)=>checks.Add(new VerificationCheck{Name=name,Passed=passed,Detail="Positioning and cached navigation"});
            string second=Path.Combine(directory,"navigation-blue.png");
            using(var image=new Bitmap(600,400)){using(var g=Graphics.FromImage(image))g.Clear(Color.Blue);image.Save(second,ImageFormat.Png);}
            using(var form=new MainForm(new[]{fixture},System.IO.Path.Combine(directory,"NavigationVerification-preferences.json")))
            {
                form.Opacity=0;form.ShowInTaskbar=false;form.Size=new Size(1040,720);form.Show();Application.DoEvents();
                form.AllPhotos.Clear();var first=new PhotoItem(fixture){Copies=1};var last=new PhotoItem(second){Copies=1};form.AllPhotos.AddRange(new[]{first,last});
                form.Options.Paper="3.5x5";form.Options.PrintInfo=false;form.CurrentLayout=FrameSpool.LayoutEngine.Catalog.First(l=>l.Id=="bo011201.fd2");
                form.BuildPrinting();form.BuildMenus();WaitUntil(()=>form.preview.RenderingReady&&form.preview.CachedPages>=2);
                var previous=form.body.Controls.OfType<SkinButton>().First(c=>c.ImagePath=="Image/btn_2-1-22_u.bmp");
                SaveSurface(form,Path.Combine(directory,"navigation-controls.png"));
                check("Trim, Fit and magnification stay below page navigation",form.inlineTrim.Top>=previous.Bottom&&form.inlineFit.Top>=previous.Bottom&&form.inlineZoom.Top>=previous.Bottom&&form.inlineZoom.Bottom<=form.totalLabel.Top);
                form.ShowPositioning();form.positioningPalette.Opacity=0;Application.DoEvents();
                SaveSurface(form.positioningPalette,Path.Combine(directory,"positioning-palette.png"));
                var paletteZoom=form.positioningPalette.Controls.OfType<TrackBar>().Single();
                checks.Add(new VerificationCheck{Name="Positioning opens inside the app beside the preview",Passed=form.positioningPalette.Left>=form.Left&&form.positioningPalette.Right<=form.preview.RectangleToScreen(form.preview.ClientRectangle).Left+form.Pixels(8),Detail="Palette "+form.positioningPalette.Bounds+", owner "+form.Bounds+", preview "+form.preview.RectangleToScreen(form.preview.ClientRectangle)});
                form.inlineZoom.Value=150;
                check("Inline magnification synchronizes the open Positioning palette",form.FocusPhoto.Zoom==1.5f&&paletteZoom.Value==150);
                paletteZoom.Value=125;
                check("Palette magnification synchronizes inline controls immediately",form.FocusPhoto.Zoom==1.25f&&form.inlineZoom.Value==125&&form.inlineZoomValue.Text=="125%");
                form.FocusPhoto.OffsetX=20;form.FocusPhoto.OffsetY=10;form.inlineFit.PerformClick();
                check("Inline Fit resets cropping and updates the palette",form.FocusPhoto.Fit&&form.FocusPhoto.Zoom==1&&form.FocusPhoto.OffsetX==0&&form.FocusPhoto.OffsetY==0&&paletteZoom.Value==100);
                form.positioningPalette.Controls.OfType<Button>().First(c=>c.Text=="Trim").PerformClick();
                check("Palette Trim updates inline mode and magnification",!form.FocusPhoto.Fit&&form.inlineZoom.Value==100&&form.inlineTrim.BackColor==Color.LightSteelBlue);
                form.pageIndex=1;form.UpdatePage();
                check("Adjacent page navigation displays the cached image immediately",form.preview.CurrentPreviewDisplayed&&form.preview.PreviewCenterColor.ToArgb()==Color.Blue.ToArgb());
                check("Navigating synchronizes positioning to the new photo",form.FocusPhoto.Original==last&&form.inlineZoom.Value==100&&paletteZoom.Value==100);
                form.inlineZoom.Value=160;
                check("Crop changes invalidate previously cached previews",!form.preview.CurrentPreviewDisplayed&&form.FocusPhoto.Zoom==1.6f);
                check("Uncached edits retain a preview instead of flashing a loading placeholder",form.preview.HasDisplayedPreview);
                WaitUntil(()=>form.preview.RenderingReady);form.pageIndex=0;form.UpdatePage();
                check("Previous page navigation reuses its correct cached preview",form.preview.CurrentPreviewDisplayed&&form.FocusPhoto.Original==first&&form.FocusPhoto.Zoom==1);
                for(int zoom=101;zoom<=110;zoom++){form.inlineZoom.Value=zoom;WaitUntil(()=>form.preview.RenderingReady);}
                check("Preview cache remains bounded after repeated crop edits",form.preview.CachedPages<=8&&form.preview.CachedBytes<=64*1024*1024);
                using(var replacement=new Bitmap(600,400)){using(var g=Graphics.FromImage(replacement))g.Clear(Color.Lime);replacement.Save(second,ImageFormat.Png);}
                File.SetLastWriteTimeUtc(second,DateTime.UtcNow.AddSeconds(2));form.pageIndex=1;form.UpdatePage();
                check("Changing a source file invalidates its cached preview",!form.preview.CurrentPreviewDisplayed);
                WaitUntil(()=>form.preview.RenderingReady);check("Replaced source files display their new pixels",form.preview.PreviewCenterColor.ToArgb()==Color.Lime.ToArgb());
                form.preview.Page.Photos.Clear();form.Focus(null);form.UpdatePage();
                check("Empty photo slots disable both positioning surfaces",!form.inlineTrim.Enabled&&!form.inlineFit.Enabled&&!form.inlineZoom.Enabled&&!paletteZoom.Enabled);
                form.positioningPalette.Close();Application.DoEvents();form.inlineZoom.Value=110;
                check("Closing Positioning leaves inline controls safe",form.positioningPalette==null&&form.syncPositioningPalette==null);
                form.Pages[1].Photos.Add(last.CreatePlacement());
                form.pageIndex=0;form.UpdatePage();form.SetPhotoFit(false);form.inlineZoom.Value=150;
                var placed=form.FocusPhoto;var sibling=first.CreatePlacement();form.Pages[0].Photos.Add(sibling);
                var sheet=form.preview.Sheet;var frame=form.preview.LayoutDefinition.Frames[0];
                var start=new Point((int)(sheet.X+(frame.X+frame.Width/2)*sheet.Width),(int)(sheet.Y+(frame.Y+frame.Height/2)*sheet.Height));
                var end=new Point(start.X+24,start.Y-18);
                var down=typeof(PagePreview).GetMethod("OnMouseDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                var move=typeof(PagePreview).GetMethod("OnMouseMove",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                var up=typeof(PagePreview).GetMethod("OnMouseUp",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                Action<Point> press=point=>down.Invoke(form.preview,new object[]{new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)});
                Action<Point> drag=point=>move.Invoke(form.preview,new object[]{new MouseEventArgs(MouseButtons.Left,0,point.X,point.Y,0)});
                Action<Point> release=point=>up.Invoke(form.preview,new object[]{new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)});
                press(start);release(start);
                check("Clicking a photo focuses it without moving the crop",form.FocusPhoto==placed&&placed.OffsetX==0&&placed.OffsetY==0&&!form.preview.IsDragging);
                press(start);drag(end);
                check("Dragging in Automatic flow moves the photo in physical page units",form.preview.IsDragging&&Math.Abs(placed.OffsetX-24*form.CurrentLayout.Width/sheet.Width)<.01&&Math.Abs(placed.OffsetY+18*form.CurrentLayout.Height/sheet.Height)<.01);
                release(end);WaitUntil(()=>form.preview.RenderingReady);
                check("Releasing a crop drag retains zoom and independent copy offsets",!form.preview.Capture&&!form.preview.IsDragging&&placed.Zoom==1.5f&&sibling.OffsetX==0&&sibling.OffsetY==0&&first.OffsetX==0&&first.OffsetY==0);
                var savedOffset=new PointF(placed.OffsetX,placed.OffsetY);press(start);drag(end);
                var message=new Message();form.ProcessCmdKey(ref message,Keys.Escape);
                check("Escape cancels the active crop drag without leaving Print Settings",form.printingScreen&&!form.preview.IsDragging&&!form.preview.Capture&&placed.OffsetX==savedOffset.X&&placed.OffsetY==savedOffset.Y);
                form.SetPhotoFit(true);press(start);drag(end);release(end);
                check("Fit photos can be moved directly without changing fit or zoom",placed.Fit&&placed.Zoom==1&&placed.OffsetX!=0&&placed.OffsetY!=0);
                form.SetPhotoFit(false);press(start);drag(end);form.preview.Capture=false;
                check("Losing mouse capture ends positioning safely",!form.preview.IsDragging);
                placed.OffsetX=placed.OffsetY=0;press(start);form.pageIndex=1;form.UpdatePage();drag(end);release(end);
                check("Changing pages during a drag never moves another photo",placed.OffsetX==0&&placed.OffsetY==0&&form.FocusPhoto.OffsetX==0&&form.FocusPhoto.OffsetY==0&&!form.preview.IsDragging);
                form.pageIndex=0;form.UpdatePage();form.ApplyDpi(144);sheet=form.preview.Sheet;frame=form.preview.LayoutDefinition.Frames[0];
                start=new Point((int)(sheet.X+(frame.X+frame.Width/2)*sheet.Width),(int)(sheet.Y+(frame.Y+frame.Height/2)*sheet.Height));end=new Point(start.X+36,start.Y+27);
                press(start);drag(end);release(end);
                check("Crop dragging remains accurate after display scaling",Math.Abs(placed.OffsetX-36*form.CurrentLayout.Width/sheet.Width)<.01&&Math.Abs(placed.OffsetY-27*form.CurrentLayout.Height/sheet.Height)<.01);
                var rotated=new FrameDesign{Width=1800,Height=1260,Paper="3.5x5"};rotated.Elements.Add(new FrameElement{X=.2f,Y=.2f,Width=.6f,Height=.6f,Rotation=90});
                form.CurrentLayout=rotated.ToLayout();form.UpdatePage();placed.OffsetX=placed.OffsetY=0;sheet=form.preview.Sheet;
                start=new Point((int)(sheet.X+sheet.Width/2),(int)(sheet.Y+sheet.Height/2));end=new Point(start.X+30,start.Y);
                press(start);drag(end);release(end);
                check("Photos follow the mouse inside rotated custom openings",Math.Abs(placed.OffsetX)<.01&&Math.Abs(placed.OffsetY+30*form.CurrentLayout.Height/sheet.Height)<.01);
                form.preview.EnableManual=true;var beforeManual=new PointF(placed.OffsetX,placed.OffsetY);press(start);release(start);
                check("Manual flow still reserves dragging for slot placement",!form.preview.Capture&&placed.OffsetX==beforeManual.X&&placed.OffsetY==beforeManual.Y);
            }
            var working=new Rectangle(-1920,0,1920,1080);var location=PositioningLocation(new Rectangle(-1900,800,900,600),new Size(360,740),working,50);
            check("Positioning stays on screen near monitor edges",working.Contains(new Rectangle(location,new Size(360,740))));
        }
    }
}
