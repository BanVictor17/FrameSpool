using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class MainForm
    {
        // Exercises our own forms in-process, without controlling other Windows
        // applications or spooling print jobs. Does not write user preferences.
        internal static void VerifyVisuals(string directory,string fixture,List<VerificationCheck> checks)
        {
            Action<string,bool> check=delegate(string name,bool passed){checks.Add(new VerificationCheck{Name=name,Passed=passed,Detail="In-process Windows Forms verification"});};
            using(var form=new MainForm(new[]{fixture}))
            {
                form.ShowInTaskbar=false;form.Opacity=0;form.Show();Application.DoEvents();form.AllPhotos.Clear();form.PerformLayout();
                var photo=new PhotoItem(fixture){Copies=1};photo.Adjustments.Mode="None";
                form.AllPhotos.Add(photo);form.ShowFolderPhotos(new List<PhotoItem>{photo});form.RefreshSelection();
                check("Startup opens only the main window",!Application.OpenForms.Cast<Form>().Any(f=>f is StartupSplash));
                check("Selection surface paints content",SaveSurface(form,System.IO.Path.Combine(directory,"select-photos.png")));
                check("Photo selection enables Next Step",form.next.Enabled&&form.selectedPhotos.Photos.Count==1);
                form.Options.Paper="3.5x5";form.CurrentLayout=FrameSpool.LayoutEngine.Catalog.First(l=>l.Id=="in201200.fd2");
                form.BuildPrinting();form.BuildMenus();form.PerformLayout();
                check("Print-settings surface paints content",SaveSurface(form,System.IO.Path.Combine(directory,"print-settings.png")));
                check("Layout and flow radio groups stay independent",form.photoLayoutRadio.Checked&&form.body.Controls.OfType<Panel>().SelectMany(p=>p.Controls.OfType<RadioButton>()).Any(r=>r.Text=="Automatic"&&r.Checked));
                check("Print preview displays the selected photo",form.preview.Page!=null&&form.preview.Page.Photos[0].Original==photo&&form.preview.Sheet.Width>0);
                form.preview.FitToWindow=false;
                check("Actual-size preview exposes a scrollable page",form.preview.AutoScrollMinSize.Height>form.preview.Height);
                form.preview.FitToWindow=true;check("Fit to Window clears page scroll range",form.preview.AutoScrollMinSize==Size.Empty);
                foreach(var size in new[]{new Size(800,600),new Size(1040,720),new Size(1280,900),new Size(800,600)})
                {
                    form.Size=size;form.PerformLayout();Application.DoEvents();
                    var outside=form.body.Controls.Cast<Control>().Where(c=>c.Left<0||c.Top<0||c.Right>form.body.ClientSize.Width||c.Bottom>form.body.ClientSize.Height).ToList();
                    checks.Add(new VerificationCheck{Name="Printing controls stay inside the window at "+size,Passed=outside.Count==0,Detail=outside.Count==0?"In-process Windows Forms verification":"Body "+form.body.ClientSize+": "+String.Join("; ",outside.Select(c=>c.GetType().Name+" "+c.Text+" "+c.Bounds))});
                    check("Print footer has no overlapping controls at "+size,form.totalLabel.Bottom<=form.dateInfo.Top);
                }
                for(int i=0;i<4;i++){form.ShowFrameMenu();form.frameMenu.Close();Application.DoEvents();}
                check("Frame menu survives repeated closing",!form.frameMenu.IsDisposed);
                form.manualMode=true;form.preview.EnableManual=true;
                var firstFrame=form.preview.LayoutDefinition.Frames[0];var sheet=form.preview.Sheet;
                var slot=Point.Round(new PointF(sheet.Left+sheet.Width*(firstFrame.X+firstFrame.Width/2),sheet.Top+sheet.Height*(firstFrame.Y+firstFrame.Height/2)));
                var valid=new DataObject();valid.SetData("FrameSpool.Slot",0);valid.SetData("FrameSpool.Page",form.preview.Page);
                check("Valid manual moves are accepted",form.preview.DropEffect(valid,slot)==DragDropEffects.Move);
                var fileDrop=new DataObject();fileDrop.SetData(DataFormats.FileDrop,new[]{fixture});
                check("Valid photo file drops are accepted",form.preview.DropEffect(fileDrop,slot)==DragDropEffects.Copy);
                var mouse=typeof(PagePreview).GetMethod("OnMouseDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                var slotMenu=(ContextMenuStrip)typeof(PagePreview).GetField("slotMenu",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(form.preview);
                for(int i=0;i<4;i++){mouse.Invoke(form.preview,new object[]{new MouseEventArgs(MouseButtons.Right,1,slot.X,slot.Y,0)});slotMenu.Close();Application.DoEvents();}
                check("Manual-slot menu survives repeated closing",!slotMenu.IsDisposed);
                check("Page margins reject drops",form.preview.DropEffect(valid,Point.Empty)==DragDropEffects.None);
                var foreign=new DataObject();foreign.SetData("FrameSpool.Slot",0);foreign.SetData("FrameSpool.Page",new PhotoPage());
                check("Drag rejects slots from a different page",form.preview.DropEffect(foreign,slot)==DragDropEffects.None);
                var emptyDrop=new DataObject();emptyDrop.SetData(DataFormats.FileDrop,new string[0]);
                check("Empty file drops are safe",form.preview.DropEffect(emptyDrop,slot)==DragDropEffects.None);
                valid.SetData("FrameSpool.Slot",-1);check("Negative source slots are rejected",form.preview.DropEffect(valid,slot)==DragDropEffects.None);
                form.AssignManualPhoto(-1,fixture);check("Negative manual placements preserve the page",form.Pages[0].Photos.Count==1);
                form.AssignManualPhoto(3,fixture);var page=form.Pages[0];
                check("Manual placement repeats a photo in another slot",page.Photos.Count==4&&page.Photos[0].Original==page.Photos[3].Original);
                page.Photos[3].Zoom=2;check("Repeated photos have independent cropping",page.Photos[0].Zoom==1&&page.Photos[3].Zoom==2&&photo.Zoom==1);
                form.RebuildPages();check("Refreshing manual preview preserves the arrangement",Object.ReferenceEquals(page,form.Pages[0])&&form.Pages[0].Photos[3].Original==photo);
                form.RebuildPages(true);check("Reset Manual Flow restores automatic placement",form.Pages[0].Photos.Count==1);
                form.manualMode=false;
                form.BuildBrowser();form.BuildMenus();form.ShowFolderPhotos(new List<PhotoItem>{photo});
                int browserControls=form.body.Controls.Count;
                form.BuildPrinting();form.BuildMenus();form.BuildBrowser();form.BuildMenus();form.ShowFolderPhotos(new List<PhotoItem>{photo});
                SaveSurface(form,System.IO.Path.Combine(directory,"select-photos-return.png"));
                check("Repeated screen changes do not duplicate controls",form.body.Controls.Count==browserControls&&!form.printingScreen);
                check("Returning from printing retains selected copies",form.Selected.Count==1&&form.next.Enabled);
                foreach(int dpi in new[]{120,144,192,96})
                {
                    form.ApplyDpi(dpi);form.ClientSize=new Size(form.Pixels(1024),form.Pixels(720));form.PerformLayout();
                    check("Browser fonts and sidebar scale at "+dpi+" DPI",Math.Abs(form.folders.Font.Size-12*dpi/96f)<.1&&form.folders.Width==form.Pixels(160));
                    form.BuildPrinting();form.BuildMenus();Application.DoEvents();
                    check("Rebuilt printing controls scale at "+dpi+" DPI",form.layouts.Width==form.Pixels(200)&&form.layouts.ItemHeight==form.Pixels(55)&&Math.Abs(form.printerInfo.Font.Size-12*dpi/96f)<.1);
                    var display=form.body.DisplayRectangle;
                    var outsideDpi=form.body.Controls.Cast<Control>().Where(c=>c.Left<display.Left||c.Top<display.Top||c.Right>display.Right||c.Bottom>display.Bottom).ToList();
                    checks.Add(new VerificationCheck{Name="Printing controls fit the visible or scrollable workspace at "+dpi+" DPI",Passed=outsideDpi.Count==0,Detail=outsideDpi.Count==0?"In-process Windows Forms verification":"Display "+display+": "+String.Join("; ",outsideDpi.Select(c=>c.GetType().Name+" "+c.Text+" "+c.Bounds))});
                    SaveSurface(form,System.IO.Path.Combine(directory,"print-settings-dpi-"+dpi+".png"));
                    form.BuildBrowser();form.BuildMenus();form.ShowFolderPhotos(new List<PhotoItem>{photo});
                }
                form.BuildPrinting();form.MinimumSize=Size.Empty;form.ClientSize=new Size(640,420);Application.DoEvents();
                check("Small laptop workspaces expose scrolling instead of shrinking controls",form.body.AutoScroll&&form.body.AutoScrollMinSize.Width>=784&&form.body.AutoScrollMinSize.Height>=521&&form.printerInfo.Font.Size==12);
                form.body.AutoScrollPosition=new Point(10000,10000);Application.DoEvents();
                var printButton=form.body.Controls.OfType<SkinButton>().First(c=>c.Text=="Print");
                check("Workspace scrolling reaches the Print button",printButton.Bottom<=form.body.ClientSize.Height&&printButton.Right<=form.body.ClientSize.Width);
            }
            var settings=new PrinterSettings();

            {
                InitializePrintRange(settings,4);
                check("Print dialog starts with a valid nonzero page range",settings.MinimumPage==1&&settings.MaximumPage==4&&settings.FromPage==1&&settings.ToPage==4&&settings.PrintRange==PrintRange.AllPages);
                InitializePrintRange(settings,1);check("Single-page print dialog bounds are valid",settings.FromPage==1&&settings.ToPage==1&&settings.MaximumPage==1);
            }
            using(var renderer=new PreviewRenderer())
            {
                var started=new ManualResetEventSlim();var finish=new ManualResetEventSlim();Bitmap stale=null;
                renderer.Request(()=>{started.Set();finish.Wait();return stale=new Bitmap(2,2);});
                WaitUntil(()=>started.IsSet);renderer.Request(()=>{var b=new Bitmap(3,3);b.SetPixel(0,0,Color.Red);return b;});finish.Set();
                WaitUntil(()=>renderer.Ready);
                check("Rapid preview changes discard stale render results",renderer.Image!=null&&renderer.Image.Width==3&&renderer.Image.GetPixel(0,0)==Color.FromArgb(255,255,0,0));
                bool disposed=false;try{int ignored=stale.Width;}catch(ArgumentException){disposed=true;}
                check("Stale render bitmaps are released",disposed);started.Dispose();finish.Dispose();
            }
            var closingRenderer=new PreviewRenderer();var closingStarted=new ManualResetEventSlim();var closingFinish=new ManualResetEventSlim();Bitmap closingImage=null;
            closingRenderer.Request(()=>{closingStarted.Set();closingFinish.Wait();return closingImage=new Bitmap(2,2);});WaitUntil(()=>closingStarted.IsSet);closingRenderer.Dispose();closingFinish.Set();
            WaitUntil(()=>closingImage!=null);Application.DoEvents();
            bool closedBitmap=false;WaitUntil(()=>{try{int ignored=closingImage.Width;}catch(ArgumentException){closedBitmap=true;}return closedBitmap;});
            check("Closing a window releases in-flight render results",closedBitmap);closingStarted.Dispose();closingFinish.Dispose();
            var design=new FrameDesign{Name="Verification Frame",Width=1800,Height=1260,Paper="3.5x5"};
            design.Elements.Add(new FrameElement{Kind="Ellipse Opening",X=.1f,Y=.1f,Width=.8f,Height=.7f});
            design.Elements.Add(new FrameElement{Kind="Text",Text="Synthetic frame",X=.1f,Y=.83f,Width=.8f,Height=.1f,Points=20});
            var copy=design.Clone();copy.Elements[0].X=.2f;check("Frame edits use an independent copy",design.Elements[0].X==.1f);
            FrameStore.Validate(design);
            using(var hitTest=new PagePreview{LayoutDefinition=design.ToLayout(),Size=new Size(500,400)})
            {
                var sheet=hitTest.Sheet;
                check("Oval openings accept their visible center",hitTest.At(Point.Round(new PointF(sheet.Left+sheet.Width*.5f,sheet.Top+sheet.Height*.45f)))==0);
                check("Oval openings reject transparent corners",hitTest.At(Point.Round(new PointF(sheet.Left+sheet.Width*.11f,sheet.Top+sheet.Height*.11f)))==-1);
            }
            using(var editor=new FrameEditor(design)){editor.ShowInTaskbar=false;editor.Opacity=0;editor.Show();Application.DoEvents();check("Frame-editor surface paints content",SaveSurface(editor,System.IO.Path.Combine(directory,"frame-editor.png")));check("Frame editor constructs without a printer",editor.Design.Elements.Count==2);}
            var pageContent=new PhotoPage();pageContent.Photos.Add(new PhotoItem(fixture));
            using(var bitmap=FrameSpool.LayoutEngine.RenderPage(design.ToLayout(),pageContent,new PrintOptions(),150,false))
            {check("Oval frame clips corners to backing paper",bitmap.GetPixel(76,54).ToArgb()==Color.White.ToArgb()&&bitmap.GetPixel(bitmap.Width/2,bitmap.Height/3).ToArgb()!=Color.White.ToArgb());bitmap.Save(System.IO.Path.Combine(directory,"custom-frame-print.png"),ImageFormat.Png);}
        }
        internal static void WaitUntil(Func<bool> ready)
        {var clock=Stopwatch.StartNew();while(!ready()){Application.DoEvents();Thread.Sleep(5);if(clock.ElapsedMilliseconds>20000)throw new TimeoutException("Preview verification timed out.");}Application.DoEvents();}
        static bool SaveSurface(Form form,string path){using(var prime=new Bitmap(form.Width,form.Height))form.DrawToBitmap(prime,new Rectangle(Point.Empty,prime.Size));WaitUntil(()=>Descendants(form).All(c=>!(c is PagePreview)||((PagePreview)c).RenderingReady)&&Descendants(form).All(c=>!(c is TileView)||((TileView)c).ThumbnailsReady)&&Descendants(form).All(c=>!(c is CorrectionCanvas)||((CorrectionCanvas)c).RenderingReady));using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(path,ImageFormat.Png);var colors=new HashSet<int>();for(int y=80;y<bitmap.Height-50;y+=4)for(int x=5;x<bitmap.Width-10;x+=4)colors.Add(bitmap.GetPixel(x,y).ToArgb());return colors.Count>20;}}
    }
}
