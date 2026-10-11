using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
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
                form.manualMode=true;form.preview.EnableManual=true;
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
            }
            var design=new FrameDesign{Name="Verification Frame",Width=1800,Height=1260,Paper="3.5x5"};
            design.Elements.Add(new FrameElement{Kind="Ellipse Opening",X=.1f,Y=.1f,Width=.8f,Height=.7f});
            design.Elements.Add(new FrameElement{Kind="Text",Text="Synthetic frame",X=.1f,Y=.83f,Width=.8f,Height=.1f,Points=20});
            var copy=design.Clone();copy.Elements[0].X=.2f;check("Frame edits use an independent copy",design.Elements[0].X==.1f);
            FrameStore.Validate(design);
            using(var editor=new FrameEditor(design)){editor.ShowInTaskbar=false;editor.Opacity=0;editor.Show();Application.DoEvents();check("Frame-editor surface paints content",SaveSurface(editor,System.IO.Path.Combine(directory,"frame-editor.png")));check("Frame editor constructs without a printer",editor.Design.Elements.Count==2);}
            var pageContent=new PhotoPage();pageContent.Photos.Add(new PhotoItem(fixture));
            using(var bitmap=FrameSpool.LayoutEngine.RenderPage(design.ToLayout(),pageContent,new PrintOptions(),150,false))
            {check("Oval frame clips corners to backing paper",bitmap.GetPixel(76,54).ToArgb()==Color.White.ToArgb()&&bitmap.GetPixel(bitmap.Width/2,bitmap.Height/3).ToArgb()!=Color.White.ToArgb());bitmap.Save(System.IO.Path.Combine(directory,"custom-frame-print.png"),ImageFormat.Png);}
        }
        static bool SaveSurface(Form form,string path){using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(path,ImageFormat.Png);var colors=new HashSet<int>();for(int y=80;y<bitmap.Height-50;y+=4)for(int x=5;x<bitmap.Width-10;x+=4)colors.Add(bitmap.GetPixel(x,y).ToArgb());return colors.Count>20;}}
    }
}
