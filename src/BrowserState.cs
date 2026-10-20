using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class MainForm
    {
        readonly List<PhotoItem> selectionOrder=new List<PhotoItem>();
        List<Tuple<PhotoItem,int>> previousSelection=new List<Tuple<PhotoItem,int>>();
        int selectionRevision, printSelectionRevision=-1;

        List<PhotoItem> OrderedSelection()
        {
            var available=new HashSet<PhotoItem>(AllPhotos);
            selectionOrder.RemoveAll(p=>p.Copies<=0||!available.Contains(p));
            var recorded=new HashSet<PhotoItem>(selectionOrder);
            foreach(var photo in AllPhotos)if(photo.Copies>0&&recorded.Add(photo))selectionOrder.Add(photo);
            if(previousSelection.Count!=selectionOrder.Count||selectionOrder.Where((p,i)=>i>=previousSelection.Count||previousSelection[i].Item1!=p||previousSelection[i].Item2!=p.Copies).Any())
            {
                selectionRevision++;previousSelection=selectionOrder.Select(p=>Tuple.Create(p,p.Copies)).ToList();
            }
            return new List<PhotoItem>(selectionOrder);
        }

        int ApplySelectAll(List<PhotoItem> targets,int value)
        {
            OrderedSelection();
            int allowed=999-AllPhotos.Count(p=>p.Copies>0&&!targets.Contains(p)),assigned=0,skipped=0;
            foreach(var photo in targets.OrderByDescending(p=>p.Copies>0))
            {
                if(value==0)photo.Copies=0;
                else if(assigned<allowed){photo.Copies=value;assigned++;}
                else{photo.Copies=0;skipped++;}
            }
            // The tile list is drawn in this order: left to right, then the next
            // row. A bulk selection must keep that order even after earlier clicks.
            selectionOrder.RemoveAll(targets.Contains);
            selectionOrder.AddRange(targets.Where(p=>p.Copies>0));
            OrderedSelection();RefreshSelection();if(printingScreen)RebuildPages();
            return skipped;
        }

        void PreparePrintSelection()
        {
            OrderedSelection();
            if(printSelectionRevision==selectionRevision)return;
            printSelectionRevision=selectionRevision;
            pageIndex=0;Pages.Clear();pageCopyCounts.Clear();FocusPhoto=null;
        }

        sealed class BrowserScreen
        {
            public Control[] Controls;
            public EventHandler Layout;
            public TreeView Folders;
            public TileView Photos,Selected;
            public Label Info,Count;
            public SkinButton Next,Enlarge,Remove;
            public CheckBox QuickPrint;
            public ToolStrip Navigation;
            public TextBox Address;
            public PhotoItem Focus;
            public string Folder;
            public TreeNode[] Expanded;
            public TreeNode SelectedNode,TopNode;
            public Point PhotoScroll,SelectedScroll,BodyScroll;
        }
        BrowserScreen cachedBrowser;
        void CacheBrowser()
        {
            if(printingScreen||folderPhotos==null||folderPhotos.IsDisposed)return;
            cachedBrowser=new BrowserScreen {Controls=body.Controls.Cast<Control>().ToArray(),Layout=bodyLayout,Folders=folders,
                Photos=folderPhotos,Selected=selectedPhotos,Info=fileInfo,Count=selectedCount,Next=next,Enlarge=enlarge,Remove=remove,
                QuickPrint=quickPrint,Navigation=browserNavigation,Address=browserAddress,Focus=FocusPhoto,Folder=currentFolder,
                Expanded=AllFolderNodes(folders.Nodes).Where(n=>n.IsExpanded).ToArray(),SelectedNode=folders.SelectedNode,TopNode=folders.TopNode,
                PhotoScroll=folderPhotos.AutoScrollPosition,SelectedScroll=selectedPhotos.AutoScrollPosition,BodyScroll=body.AutoScrollPosition};
            body.Controls.Clear();
        }
        bool RestoreCachedBrowser()
        {
            if(cachedBrowser==null)return false;
            var saved=cachedBrowser;cachedBrowser=null;
            folders=saved.Folders;folderPhotos=saved.Photos;selectedPhotos=saved.Selected;fileInfo=saved.Info;selectedCount=saved.Count;
            next=saved.Next;enlarge=saved.Enlarge;remove=saved.Remove;quickPrint=saved.QuickPrint;browserNavigation=saved.Navigation;browserAddress=saved.Address;
            body.Controls.AddRange(saved.Controls);bodyLayout=saved.Layout;body.Resize+=bodyLayout;body.Paint+=PaintBrowserFooter;
            folderPhotos.TileSize=browserThumbnailSize;folderPhotos.OneClickSelection=browserOneClickSelection;
            var registered=ReadQuickPrint();quickPrint.Visible=registered.A!=null||registered.B!=null;if(!quickPrint.Visible)quickPrint.Checked=false;
            syncingFolders=true;
            try{foreach(var node in saved.Expanded)node.Expand();folders.SelectedNode=saved.SelectedNode;if(saved.TopNode!=null)folders.TopNode=saved.TopNode;}
            finally{syncingFolders=false;}
            if(!SameFolder(saved.Folder,currentFolder))SyncFolderTree(currentFolder);
            Focus(saved.Focus);FinishScreen();RefreshSelection();UpdateExplorerNavigation();
            folderPhotos.AutoScrollPosition=new Point(-saved.PhotoScroll.X,-saved.PhotoScroll.Y);
            selectedPhotos.AutoScrollPosition=new Point(-saved.SelectedScroll.X,-saved.SelectedScroll.Y);
            body.AutoScrollPosition=new Point(-saved.BodyScroll.X,-saved.BodyScroll.Y);
            UpdateFolderWatchers();
            return true;
        }
        void DisposeCachedBrowser()
        {
            if(cachedBrowser==null)return;
            foreach(var control in cachedBrowser.Controls)control.Dispose();cachedBrowser=null;
        }
    }
}
