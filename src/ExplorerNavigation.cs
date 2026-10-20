using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class MainForm
    {
        ToolStrip browserNavigation;
        ToolStripButton browserBack,browserForward,browserUp;
        TextBox browserAddress;
        readonly List<string> folderHistory=new List<string>();
        int folderHistoryIndex=-1;
        bool syncingFolders;

        void BuildExplorerNavigation()
        {
            browserNavigation=new ToolStrip {GripStyle=ToolStripGripStyle.Hidden,AutoSize=false,Dock=DockStyle.None,
                Padding=Padding.Empty,CanOverflow=false,Font=Ui.Font(14),Renderer=new ToolStripSystemRenderer()};
            Func<string,string,Action,ToolStripButton> button=(text,label,action)=>
            {
                var item=new ToolStripButton(text){AccessibleName=label,ToolTipText=label,AutoSize=false,Width=30,DisplayStyle=ToolStripItemDisplayStyle.Text};
                item.Click+=delegate{action();};browserNavigation.Items.Add(item);return item;
            };
            browserBack=button("←","Back (Alt+Left)",()=>MoveFolderHistory(-1));
            browserForward=button("→","Forward (Alt+Right)",()=>MoveFolderHistory(1));
            browserUp=button("↑","Parent folder (Alt+Up)",GoToParentFolder);
            button("↻","Refresh folders and photos (F5)",RefreshExplorer);
            button("…","Choose a folder",OpenFolder);
            browserAddress=new TextBox{Font=Ui.Font(12),AccessibleName="Folder address"};
            browserAddress.KeyDown+=delegate(object sender,KeyEventArgs e)
            {
                if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;LoadFolder(browserAddress.Text);}
                else if(e.KeyCode==Keys.Escape){e.SuppressKeyPress=true;UpdateExplorerNavigation();folders.Focus();}
            };
            body.Controls.Add(browserNavigation);body.Controls.Add(browserAddress);
            var context=new ContextMenuStrip();
            context.Items.Add("Open folder",null,delegate{if(folders.SelectedNode?.Tag is string path)LoadFolder(path);});
            context.Items.Add("Refresh",null,delegate{RefreshExplorer();});
            context.Items.Add("Expand",null,delegate{folders.SelectedNode?.Expand();});
            context.Items.Add("Collapse",null,delegate{folders.SelectedNode?.Collapse();});
            context.Items.Add("Copy folder path",null,delegate{if(folders.SelectedNode?.Tag is string path)try{Clipboard.SetText(path);}catch(System.Runtime.InteropServices.ExternalException){}});
            context.Opening+=delegate{for(int i=0;i<context.Items.Count;i++)context.Items[i].Enabled=i==1||((i==2||i==3)?folders.SelectedNode!=null:folders.SelectedNode?.Tag is string);};
            folders.NodeMouseClick+=delegate(object sender,TreeNodeMouseClickEventArgs e)
            {
                if(e.Button==MouseButtons.Left&&e.Node.Tag is string path&&!SameFolder(path,currentFolder)){LoadFolder(path);return;}
                if(e.Button!=MouseButtons.Right)return;
                syncingFolders=true;try{folders.SelectedNode=e.Node;}finally{syncingFolders=false;}
            };
            folders.ContextMenuStrip=context;folders.Disposed+=delegate{context.Dispose();};
            tips.SetToolTip(browserAddress,"Type or paste a folder path and press Enter. Escape restores the current address.");
            UpdateExplorerNavigation();
        }

        static string FolderPath(string path)
        {
            string full=Path.GetFullPath(path);string root=Path.GetPathRoot(full);
            return full.Length>root.Length?full.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar):root;
        }
        static bool SameFolder(string a,string b){return String.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
        void RecordFolderHistory(string path)
        {
            if(folderHistoryIndex>=0&&SameFolder(folderHistory[folderHistoryIndex],path))return;
            if(folderHistoryIndex+1<folderHistory.Count)folderHistory.RemoveRange(folderHistoryIndex+1,folderHistory.Count-folderHistoryIndex-1);
            folderHistory.Add(path);if(folderHistory.Count>100)folderHistory.RemoveAt(0);
            folderHistoryIndex=folderHistory.Count-1;
        }
        bool NavigateFolder(string path,bool record)
        {
            if(String.IsNullOrWhiteSpace(path))return false;
            string normalized;
            try
            {
                path=Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                if(!Path.IsPathRooted(path))path=Path.Combine(currentFolder??Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),path);
                normalized=FolderPath(path);
                if(!Directory.Exists(normalized))throw new DirectoryNotFoundException("This folder is unavailable. Reconnect the drive or choose another folder.");
            }
            catch(Exception error)when(error is ArgumentException||error is IOException||error is UnauthorizedAccessException||error is NotSupportedException)
            {UpdateExplorerNavigation();ShowBrowserAlert(error.Message,"Folder");return false;}
            currentFolder=normalized;if(record)RecordFolderHistory(normalized);
            SyncFolderTree(normalized);UpdateExplorerNavigation();
            try{ImportFiles(new[]{normalized},false,false);}catch(Exception error){ShowBrowserAlert(error.Message,"Folder");}
            return true;
        }
        void MoveFolderHistory(int direction)
        {
            int index=folderHistoryIndex+direction;if(index<0||index>=folderHistory.Count)return;
            if(NavigateFolder(folderHistory[index],false)){folderHistoryIndex=index;UpdateExplorerNavigation();}
        }
        void GoToParentFolder()
        {
            try{var parent=String.IsNullOrEmpty(currentFolder)?null:Directory.GetParent(currentFolder);if(parent!=null)LoadFolder(parent.FullName);}catch(IOException){}
        }
        void UpdateExplorerNavigation()
        {
            if(browserNavigation==null||browserNavigation.IsDisposed||browserAddress==null||browserAddress.IsDisposed)return;
            browserBack.Enabled=folderHistoryIndex>0;browserForward.Enabled=folderHistoryIndex>=0&&folderHistoryIndex<folderHistory.Count-1;
            browserUp.Enabled=!String.IsNullOrEmpty(currentFolder)&&!SameFolder(currentFolder,Path.GetPathRoot(currentFolder));
            browserAddress.Text=currentFolder??"";tips.SetToolTip(browserAddress,currentFolder??"Type a folder path and press Enter.");
        }
        void LayoutExplorerNavigation()
        {
            if(browserNavigation==null||browserNavigation.IsDisposed)return;
            foreach(ToolStripItem item in browserNavigation.Items)item.Size=new Size(Pixels(30),Pixels(24));
        }
        void PopulateFolderNode(TreeNode node,bool refresh)
        {
            if(!(node.Tag is string path)||!refresh&&!(node.Nodes.Count==1&&node.Nodes[0].Tag==null))return;
            try
            {
                var paths=Directory.EnumerateDirectories(path,"*",new EnumerationOptions{IgnoreInaccessible=true,AttributesToSkip=FileAttributes.Hidden|FileAttributes.System})
                    .OrderBy(p=>Path.GetFileName(p),StringComparer.CurrentCultureIgnoreCase).ToArray();
                var expanded=new HashSet<string>(AllFolderNodes(node.Nodes).Where(n=>n.IsExpanded&&n.Tag is string).Select(n=>(string)n.Tag),StringComparer.OrdinalIgnoreCase);
                var existing=node.Nodes.Cast<TreeNode>().Where(n=>n.Tag is string).ToDictionary(n=>(string)n.Tag,StringComparer.OrdinalIgnoreCase);
                foreach(var child in node.Nodes.Cast<TreeNode>().ToArray())if(!(child.Tag is string childPath)||!paths.Contains(childPath,StringComparer.OrdinalIgnoreCase))child.Remove();
                for(int i=0;i<paths.Length;i++)
                {
                    TreeNode child;if(!existing.TryGetValue(paths[i],out child)){AddFolderNode(node.Nodes,Path.GetFileName(paths[i]),paths[i],1);child=node.Nodes[node.Nodes.Count-1];}
                    if(child.Index!=i){child.Remove();node.Nodes.Insert(i,child);}
                }
                foreach(var child in AllFolderNodes(node.Nodes).ToArray())if(child.Tag is string childPath&&expanded.Contains(childPath))child.Expand();
                node.ToolTipText=path;
            }
            catch(Exception error)when(error is IOException||error is UnauthorizedAccessException)
            {if(node.Nodes.Count==0||node.Nodes[0].Tag==null){node.Nodes.Clear();node.Nodes.Add(new TreeNode("Folder unavailable"){ForeColor=Color.Gray});}node.ToolTipText=path+" — "+error.Message;}
        }
        static IEnumerable<TreeNode> AllFolderNodes(TreeNodeCollection nodes)
        {
            foreach(TreeNode node in nodes){yield return node;foreach(var child in AllFolderNodes(node.Nodes))yield return child;}
        }
        void SyncFolderTree(string path)
        {
            if(String.IsNullOrEmpty(path)||folders==null||folders.IsDisposed)return;
            syncingFolders=true;folders.BeginUpdate();
            try
            {
                var node=AllFolderNodes(folders.Nodes).Where(n=>n.Tag is string root&&(SameFolder(root,path)||path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+"\\",StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(n=>((string)n.Tag).Length).FirstOrDefault();
                if(node==null){string root=Path.GetPathRoot(path);AddFolderNode(folders.Nodes,root,root,0);node=folders.Nodes[folders.Nodes.Count-1];}
                string relative=Path.GetRelativePath((string)node.Tag,path);
                if(relative!=".")foreach(string segment in relative.Split(new[]{'\\','/'},StringSplitOptions.RemoveEmptyEntries))
                {
                    PopulateFolderNode(node,false);node.Expand();string childPath=FolderPath(Path.Combine((string)node.Tag,segment));
                    var child=node.Nodes.Cast<TreeNode>().FirstOrDefault(n=>n.Tag is string value&&SameFolder(FolderPath(value),childPath));
                    if(child==null){AddFolderNode(node.Nodes,segment,childPath,1);child=node.Nodes[node.Nodes.Count-1];}node=child;
                }
                folders.SelectedNode=node;node.EnsureVisible();
            }
            finally{folders.EndUpdate();syncingFolders=false;}
        }
        void RefreshExplorer()
        {
            if(printingScreen||folders==null||folders.IsDisposed)return;
            syncingFolders=true;folders.BeginUpdate();
            try
            {
                var computer=folders.Nodes.Cast<TreeNode>().FirstOrDefault(n=>n.Tag==null&&n.Text=="Computer");
                if(computer!=null)
                {
                    var drives=DriveInfo.GetDrives().Select(d=>d.Name).ToArray();
                    foreach(var node in computer.Nodes.Cast<TreeNode>().ToArray())if(node.Tag is string path&&!drives.Contains(path,StringComparer.OrdinalIgnoreCase))node.Remove();
                    foreach(var drive in drives)if(!computer.Nodes.Cast<TreeNode>().Any(n=>SameFolder(n.Tag as string,drive)))AddFolderNode(computer.Nodes,drive,drive,0);
                }
                // Refresh every populated branch, including Desktop when another
                // folder is selected. Keep existing nodes and their expansion state.
                foreach(var node in ObservedFolderNodes().ToArray())
                    if(node.TreeView==folders)PopulateFolderNode(node,true);
            }
            finally{folders.EndUpdate();syncingFolders=false;}
            UpdateFolderWatchers();
            RefreshCurrentFolder(true);
        }
    }
}
