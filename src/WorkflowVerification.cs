using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class MainForm
    {
        internal static void VerifyWorkflows(string directory,string fixture,List<VerificationCheck> checks)
        {
            Action<string,bool> check=delegate(string name,bool passed){checks.Add(new VerificationCheck{Name=name,Passed=passed,Detail="Independent workflow integration verification"});};
            string temporary=Path.Combine(directory,"workflow-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
            try
            {
                var launch=QuickPrintLaunch.Parse(new[]{"--quick-print","b",fixture,fixture,temporary});
                check("Quick Print drag/drop launch preserves photos and folders",launch.Slot=="B"&&launch.Files.Length==2&&launch.Files.Contains(Path.GetFullPath(fixture))&&launch.Files.Contains(Path.GetFullPath(temporary)));
                check("Quick Print named launch accepts lower-case slot",QuickPrintLaunch.Parse(new[]{"--quick-print=a"}).Slot=="A");
                bool rejected=false;try{QuickPrintLaunch.Parse(new[]{"--quick-print","C"});}catch(ArgumentException){rejected=true;}check("Quick Print rejects an invalid settings slot",rejected);
                string saved=Path.Combine(temporary,"preferences.json");WorkflowStorage.Save(saved,new BrowserPreferences{ShowAlerts=false,ShowToolTips=false,ThumbnailSize=220});WorkflowStorage.Save(saved,new BrowserPreferences{ShowAlerts=true,Sort="Photo Date",SortDescending=true});
                var prefs=new JavaScriptSerializer().Deserialize<BrowserPreferences>(File.ReadAllText(saved));check("Preferences replacement keeps valid complete JSON",prefs.ShowAlerts&&prefs.Sort=="Photo Date"&&prefs.SortDescending&&Directory.GetFiles(temporary,"*.tmp").Length==0);
                var older=new JavaScriptSerializer().Deserialize<BrowserPreferences>("{\"OneClickSelection\":false}");check("Older preferences retain enabled alert and tooltip defaults",older.ShowAlerts&&older.ShowToolTips&&older.ThumbnailSize==130);
                string shortcut=Path.Combine(temporary,"test.lnk");QuickPrintShortcut.Create(shortcut,"B");check("Desktop shortcut retains executable and registered settings",QuickPrintShortcut.IsRegisteredShortcut(shortcut,"B")&&!QuickPrintShortcut.IsRegisteredShortcut(shortcut,"A"));
                using(var form=new MainForm(new[]{fixture}))
                {
                    form.ShowInTaskbar=false;form.Opacity=0;form.Show();Application.DoEvents();form.AllPhotos.Clear();form.browserShowAlerts=false;form.loadMinimumImageSize=60;
                    var a=new PhotoItem(fixture){Copies=1};form.AllPhotos.Add(a);form.ShowFolderPhotos(new List<PhotoItem>{a});
                    var message=new Message();form.ProcessCmdKey(ref message,Keys.Space);check("Space toggles photo selection without changing its source",a.Copies==0);form.ProcessCmdKey(ref message,Keys.Space);check("Space restores photo selection",a.Copies==1);
                    form.Options.Paper="3.5x5";var design=new FrameDesign{Id=Guid.NewGuid().ToString("N"),Name="Workflow Frame",Paper="3.5x5",Width=1800,Height=1260};design.Elements.Add(new FrameElement());var layout=design.ToLayout();FrameSpool.LayoutEngine.Catalog.Add(layout);form.CurrentLayout=layout;form.BuildPrinting();
                    check("Saved frame layout survives entering Print Settings",form.CurrentLayout!=null&&form.CurrentLayout.Id==layout.Id&&form.frameLayoutRadio.Checked);
                    form.pageCopies.Value=0;check("Zero page copies disables output for that page",form.Pages.Count==1&&form.Pages[0].Copies==0&&form.totalLabel.Text.Contains("0"));
                    form.ReturnToBrowser();form.AllPhotos.Clear();var paths=new List<string>();using(var image=new Bitmap(80,80)){using(var graphics=Graphics.FromImage(image))graphics.Clear(Color.CornflowerBlue);for(int i=0;i<70;i++){string path=Path.Combine(temporary,"batch-"+i.ToString("000")+".png");image.Save(path,ImageFormat.Png);paths.Add(path);}}
                    form.ImportFiles(paths.ToArray(),true);var deadline=DateTime.UtcNow.AddSeconds(20);while(form.importing&&DateTime.UtcNow<deadline){Application.DoEvents();Thread.Sleep(5);}check("Large photo batch loads asynchronously with all selections",!form.importing&&form.Selected.Count==70&&form.folderPhotos.Photos.Count==70);
                    form.ImportFiles(paths.ToArray(),false);if(form.importCancellation!=null)form.importCancellation.Cancel();deadline=DateTime.UtcNow.AddSeconds(20);while(form.importing&&DateTime.UtcNow<deadline){Application.DoEvents();Thread.Sleep(5);}check("Cancelling a batch preserves previous photo selections",!form.importing&&form.Selected.Count==70);
                    form.pendingQuickPrint="A";form.ImportFiles(paths.ToArray(),false);if(form.importCancellation!=null)form.importCancellation.Cancel();deadline=DateTime.UtcNow.AddSeconds(20);while(form.importing&&DateTime.UtcNow<deadline){Application.DoEvents();Thread.Sleep(5);}check("Cancelling a Quick Print import clears its pending print action",!form.importing&&form.pendingQuickPrint==null);
                }
            }
            catch(Exception error){checks.Add(new VerificationCheck{Name="Workflow integration verification",Passed=false,Detail=error.ToString()});}
            finally{try{Directory.Delete(temporary,true);}catch{}}
        }
    }
}
