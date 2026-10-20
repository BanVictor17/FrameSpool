using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class MainForm
    {
        readonly Dictionary<string,FileSystemWatcher> folderWatchers=new Dictionary<string,FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
        System.Windows.Forms.Timer explorerTimer;
        int explorerChanges;
        bool browsingFolder;

        IEnumerable<TreeNode> ObservedFolderNodes()
        {
            return AllFolderNodes(folders.Nodes).Where(n=>n.Tag is string&&(n.Parent==null||n.IsExpanded||n.Nodes.Count==0||n.Nodes[0].Tag is string||n==folders.SelectedNode));
        }
        void UpdateFolderWatchers()
        {
            if(printingScreen||folders==null||folders.IsDisposed)return;
            if(explorerTimer==null)
            {
                explorerTimer=new System.Windows.Forms.Timer{Interval=300};
                explorerTimer.Tick+=delegate{ProcessExplorerChanges();};explorerTimer.Start();
            }
            var paths=new HashSet<string>(ObservedFolderNodes().Select(n=>FolderPath((string)n.Tag)),StringComparer.OrdinalIgnoreCase);
            if(!String.IsNullOrEmpty(currentFolder))paths.Add(FolderPath(currentFolder));
            foreach(var path in folderWatchers.Keys.Where(p=>!paths.Contains(p)||!Directory.Exists(p)).ToArray())
            {folderWatchers[path].Dispose();folderWatchers.Remove(path);}
            foreach(var path in paths.Where(p=>!folderWatchers.ContainsKey(p)&&Directory.Exists(p)))
            {
                FileSystemWatcher watcher=null;
                try
                {
                    // Watch only the displayed directories, not entire drive trees.
                    watcher=new FileSystemWatcher(path){NotifyFilter=NotifyFilters.DirectoryName|NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size};
                    watcher.Created+=ExplorerFolderChanged;watcher.Deleted+=ExplorerFolderChanged;watcher.Renamed+=ExplorerFolderChanged;
                    watcher.Changed+=delegate(object sender,FileSystemEventArgs e){if(PhotoEngine.CanDecode(e.FullPath))ExplorerFolderChanged(sender,e);};
                    watcher.Error+=delegate{Interlocked.Exchange(ref explorerChanges,2);};
                    watcher.EnableRaisingEvents=true;folderWatchers.Add(path,watcher);
                }
                catch(Exception error)when(error is IOException||error is UnauthorizedAccessException||error is ArgumentException)
                {watcher?.Dispose();}
            }
        }
        void ExplorerFolderChanged(object sender,FileSystemEventArgs e)
        {
            // FileSystemWatcher callbacks run off the UI thread. The timer batches
            // bursts and owns all access to controls, photos and watcher lifetimes.
            Interlocked.CompareExchange(ref explorerChanges,1,0);
        }
        void ProcessExplorerChanges()
        {
            if(printingScreen||importing||IsDisposed||Disposing||folders==null||folders.IsDisposed)return;
            int changes=Interlocked.Exchange(ref explorerChanges,0);if(changes==0)return;
            if(changes==2){foreach(var watcher in folderWatchers.Values)watcher.Dispose();folderWatchers.Clear();}
            var selected=folders.SelectedNode;var top=folders.TopNode;
            syncingFolders=true;folders.BeginUpdate();
            try
            {
                foreach(var node in ObservedFolderNodes().ToArray())
                    if(node.TreeView==folders)PopulateFolderNode(node,true);
                if(selected?.TreeView==folders)folders.SelectedNode=selected;
                if(top?.TreeView==folders)folders.TopNode=top;
            }
            finally{folders.EndUpdate();syncingFolders=false;}
            UpdateFolderWatchers();RefreshCurrentFolder(false);
        }
        void RefreshCurrentFolder(bool force)
        {
            if(String.IsNullOrEmpty(currentFolder))return;
            string path=currentFolder;
            try
            {
                while(!Directory.Exists(path))
                {var parent=Directory.GetParent(path);if(parent==null)return;path=parent.FullName;}
                if(!SameFolder(path,currentFolder)){NavigateFolder(path,false);return;}
                if(!force)
                {
                    if(!browsingFolder)return;
                    var files=Directory.EnumerateFiles(path).Where(PhotoEngine.CanDecode).Take(999).Select(Path.GetFullPath);
                    if(new HashSet<string>(folderPhotos.Photos.Select(p=>p.FilePath),StringComparer.OrdinalIgnoreCase).SetEquals(files))return;
                }
                var focused=FocusPhoto;var scroll=folderPhotos.AutoScrollPosition;
                if(force)NavigateFolder(path,false);else ImportFiles(new[]{path},false,false,true);
                if(!importing)
                {
                    if(focused!=null&&folderPhotos.Photos.Contains(focused))Focus(focused);
                    folderPhotos.AutoScrollPosition=new Point(-scroll.X,-scroll.Y);
                }
            }
            catch(Exception error)when(error is IOException||error is UnauthorizedAccessException){}
        }
        void DisposeExplorerMonitoring()
        {
            explorerTimer?.Dispose();explorerTimer=null;
            foreach(var watcher in folderWatchers.Values)watcher.Dispose();folderWatchers.Clear();
        }
    }
}
