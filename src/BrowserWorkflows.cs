using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class MainForm
    {
        static DateTime SafeModified(string path) { try { return File.GetLastWriteTime(path); } catch { return DateTime.MinValue; } }
        void ShowBrowserAlert(string message, string title)
        { if (browserShowAlerts) MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        void OpenPhotos()
        {
            using (var dialog = new OpenFileDialog { Multiselect = true, Filter = "Photos|" + String.Join(";", PhotoEngine.SupportedExtensions.Select(e=>"*"+e)) + "|All files|*.*", Title="Open Photos" })
                if (dialog.ShowDialog(this) == DialogResult.OK) ImportFiles(dialog.FileNames,true);
        }
        void OpenFolder()
        {
            using(var dialog=new FolderBrowserDialog{Description="Choose a folder of photos.",SelectedPath=currentFolder??""})
                if(dialog.ShowDialog(this)==DialogResult.OK)LoadFolder(dialog.SelectedPath);
        }
        void FinishImport(List<PhotoItem> visible,bool select,int invalid,bool limited)
        {
            int selected=Selected.Count, skipped=0;
            foreach(var photo in visible)
            {
                if(!AllPhotos.Contains(photo))AllPhotos.Add(photo);
                if(select&&photo.Copies==0){if(selected<999){photo.Copies=1;selected++;}else skipped++;}
            }
            launchPhotos.Clear();launchPhotos.AddRange(visible);
            if(!printingScreen)ShowFolderPhotos(visible);else RebuildPages();RefreshSelection();
            if(select&&invalid>0)ShowBrowserAlert(invalid+" photo(s) could not be loaded. Check the format, decoder and image dimensions.","Open Photos");
            if(select&&visible.Count==0&&invalid==0)ShowBrowserAlert("No supported photos were found in the selected files or folders.","Open Photos");
            if(limited||skipped>0)ShowBrowserAlert("The maximum number of readable image files is 999. Additional files were not added.","Open Photos");
            if(pendingQuickPrint!=null){string slot=pendingQuickPrint;pendingQuickPrint=null;StartQuickPrint(slot);}
        }
        async void LoadPhotoBatch(List<string> paths,bool select,int skipped,bool limited)
        {
            var cancellation=new CancellationTokenSource();importCancellation=cancellation;importing=true;
            var existing=AllPhotos.GroupBy(p=>p.FilePath,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.OrdinalIgnoreCase);
            int minimum=loadMinimumImageSize;
            bool completed=false;
            var progress=Ui.Dialog("Loading Photos",370,118);progress.FormBorderStyle=FormBorderStyle.FixedToolWindow;progress.ShowInTaskbar=false;progress.Opacity=Opacity;
            var status=Ui.Label(progress,"Reading photo information...",15,15,340,20);
            var bar=new ProgressBar{Bounds=new Rectangle(15,42,340,16),Maximum=Math.Max(1,paths.Count)};progress.Controls.Add(bar);
            var cancel=Ui.Button(progress,"Cancel",265,79,90,23,delegate{cancellation.Cancel();});progress.CancelButton=cancel;
            progress.FormClosing+=delegate{cancellation.Cancel();};progress.Show(this);
            var reporter=new Progress<int>(count=>{if(!progress.IsDisposed){bar.Value=Math.Min(bar.Maximum,count);status.Text="Reading photo "+count+" of "+paths.Count;}});
            try
            {
                // Let the progress window paint and a queued Cancel take effect before decoding.
                await Task.Yield();cancellation.Token.ThrowIfCancellationRequested();
                var loaded=await Task.Run(delegate
                {
                    var found=new List<PhotoItem>();int count=0;
                    foreach(string path in paths)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();PhotoItem photo;
                        if(!existing.TryGetValue(path,out photo))try{photo=new PhotoItem(path);if(PhotoEngine.ValidateDimensions(photo.Width,photo.Height,minimum)!=null){skipped++;photo=null;}}catch{skipped++;photo=null;}
                        if(photo!=null)found.Add(photo);
                        if(++count%5==0||count==paths.Count)((IProgress<int>)reporter).Report(count);
                    }
                    return found;
                },cancellation.Token);
                if(!IsDisposed&&!cancellation.IsCancellationRequested&&Object.ReferenceEquals(importCancellation,cancellation)){completed=true;FinishImport(loaded,select,skipped,limited);}
            }
            catch(OperationCanceledException){}
            catch(Exception error){if(!IsDisposed)MessageBox.Show(this,"Cannot load the photos.\r\n"+error.Message,"Open Photos",MessageBoxButtons.OK,MessageBoxIcon.Information);}
            finally
            {
                if(!progress.IsDisposed){progress.Close();progress.Dispose();}
                if(Object.ReferenceEquals(importCancellation,cancellation)){importCancellation=null;importing=false;if(pendingQuickPrint!=null&&!IsDisposed){string slot=pendingQuickPrint;pendingQuickPrint=null;if(completed)StartQuickPrint(slot);}}
                cancellation.Dispose();
            }
        }
        public void StartQuickPrint(string slot)
        {
            slot=String.Equals(slot,"B",StringComparison.OrdinalIgnoreCase)?"B":"A";
            if(importing){pendingQuickPrint=slot;return;}
            if(launchPhotos.Count>0)
            {
                int count=Selected.Count;
                foreach(var photo in launchPhotos)if(photo.Copies==0&&count<999){photo.Copies=1;count++;}
                RefreshSelection();
            }
            if(Selected.Count==0){pendingQuickPrint=slot;OpenPhotos();if(!importing&&Selected.Count==0)pendingQuickPrint=null;return;}
            RunQuickPrint(slot);
        }
        protected override bool ProcessCmdKey(ref Message message,Keys keys)
        {
            if(preview!=null&&!preview.IsDisposed&&preview.IsDragging&&(keys==Keys.PageUp||keys==Keys.PageDown||keys==Keys.Escape))return base.ProcessCmdKey(ref message,keys);
            if(ActiveControl is TextBoxBase||ActiveControl is NumericUpDown||ActiveControl is ComboBox)return base.ProcessCmdKey(ref message,keys);
            if(keys==Keys.Delete&&!printingScreen){RemoveFocused();return true;}
            if(printingScreen&&keys==Keys.Escape){ReturnToBrowser();return true;}
            if(printingScreen&&(keys==Keys.PageDown||keys==Keys.PageUp)){pageIndex=Math.Max(0,Math.Min(Pages.Count-1,pageIndex+(keys==Keys.PageDown?1:-1)));UpdatePage();return true;}
            if(!printingScreen&&folderPhotos!=null&&(keys==Keys.Left||keys==Keys.Right||keys==Keys.Up||keys==Keys.Down||keys==Keys.Home||keys==Keys.End))
            {
                if(folders!=null&&folders.ContainsFocus)return base.ProcessCmdKey(ref message,keys);
                var tiles=selectedPhotos.ContainsFocus?selectedPhotos:folderPhotos;var photos=tiles.Photos;if(photos.Count==0)return true;
                int index=Math.Max(0,photos.IndexOf(FocusPhoto));int columns=tiles==selectedPhotos?1:Math.Max(1,(tiles.ClientSize.Width-20)/(tiles.TileSize+8));
                if(keys==Keys.Home)index=0;else if(keys==Keys.End)index=photos.Count-1;else index=Math.Max(0,Math.Min(photos.Count-1,index+(keys==Keys.Left?-1:keys==Keys.Right?1:keys==Keys.Up?-columns:columns)));
                Focus(photos[index]);tiles.Focus();int row=index/columns;int cardHeight=tiles==selectedPhotos?Math.Max(100,Math.Max(85,tiles.ClientSize.Width-22)+20):tiles.TileSize+2;
                int top=5+row*(cardHeight+8),scroll=-tiles.AutoScrollPosition.Y;if(top<scroll)tiles.AutoScrollPosition=new Point(0,top);else if(top+cardHeight>scroll+tiles.ClientSize.Height)tiles.AutoScrollPosition=new Point(0,top+cardHeight-tiles.ClientSize.Height);
                return true;
            }
            if(!printingScreen&&keys==Keys.Space&&FocusPhoto!=null){if(FocusPhoto.Copies==0&&Selected.Count>=999){ShowBrowserAlert("You can select up to 999 photos.","Photos to Print");return true;}FocusPhoto.Copies=FocusPhoto.Copies==0?1:0;RefreshSelection();return true;}
            return base.ProcessCmdKey(ref message,keys);
        }
    }
}
