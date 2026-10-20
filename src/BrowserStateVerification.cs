using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class MainForm
    {
        internal static void VerifyBrowserState(string directory,string fixture,List<VerificationCheck> checks)
        {
            Action<string,bool> check=(name,passed)=>checks.Add(new VerificationCheck{Name=name,Passed=passed,Detail="Browser state, selection order and window persistence"});
            string root=Path.Combine(directory,"browser-state");Directory.CreateDirectory(root);
            string child=Path.Combine(root,"Child"),grandchild=Path.Combine(child,"Nested");Directory.CreateDirectory(grandchild);
            string prefs=Path.Combine(directory,"browser-state-preferences.json");
            try
            {
                for(int i=23;i>=0;i--)
                using(var image=new Bitmap(90,80)){using(var g=Graphics.FromImage(image))g.Clear(Color.FromArgb(20+i*8,30,40));image.Save(Path.Combine(root,i.ToString("00")+".png"),ImageFormat.Png);}
                using(var form=new MainForm(new[]{fixture},prefs))
                {
                    form.Opacity=0;form.ShowInTaskbar=false;form.Show();Application.DoEvents();form.browserShowAlerts=false;form.loadMinimumImageSize=60;
                    form.AllPhotos.Clear();form.LoadFolder(root);var visible=form.folderPhotos.Photos.ToArray();
                    check("Browser displays a stable visual filename order",visible.Length==24&&visible[0].FileName=="00.png"&&visible[23].FileName=="23.png");
                    var clicked=new[]{visible[5],visible[0],visible[3]};
                    foreach(var photo in clicked){photo.Copies=1;form.RefreshSelection();}
                    check("Individual photo selections retain click order",form.Selected.SequenceEqual(clicked));
                    form.ApplySelectAll(visible.ToList(),0);var imported=new[]{visible[20],visible[10],visible[15]};
                    form.ImportFiles(imported.Select(p=>p.FilePath).ToArray(),true);
                    check("Importing previously browsed photos retains the supplied selection order",form.Selected.SequenceEqual(imported));form.LoadFolder(root);
                    check("Folder Quick Print candidates follow the visible browser order",form.launchPhotos.SequenceEqual(form.folderPhotos.Photos));
                    form.ApplySelectAll(form.folderPhotos.Photos,2);
                    check("Select All follows left-to-right, row-by-row tile order",form.Selected.SequenceEqual(visible)&&form.selectedPhotos.Photos.SequenceEqual(visible));
                    var layout=FrameSpool.LayoutEngine.Catalog.First(l=>l.Id=="bo011201.fd2");
                    var pages=FrameSpool.LayoutEngine.Paginate(form.Selected,layout);
                    check("Print pagination keeps selection order and adjacent photo copies",pages.SelectMany(p=>p.Photos).Select(p=>p.Original??p).SequenceEqual(visible.SelectMany(p=>new[]{p,p})));
                    form.SetPhotoSort("File Name",true);var descending=form.folderPhotos.Photos.ToArray();form.ApplySelectAll(form.folderPhotos.Photos,1);
                    check("Select All honors descending browser sort",form.Selected.SequenceEqual(descending)&&descending[0]==visible[23]);
                    form.ApplySelectAll(form.folderPhotos.Photos,0);foreach(var photo in visible.Take(3)){photo.Copies=1;form.RefreshSelection();}
                    form.LoadFolder(child);form.GoToParentFolder();
                    check("Up navigation loads the parent folder",SameFolder(form.currentFolder,root));
                    form.MoveFolderHistory(-1);
                    check("Back navigation restores the previous folder and enables Forward",SameFolder(form.currentFolder,child)&&form.browserForward.Enabled);
                    form.MoveFolderHistory(1);check("Forward navigation restores the folder without rewriting history",SameFolder(form.currentFolder,root)&&!form.browserForward.Enabled);
                    form.LoadFolder(child);form.LoadFolder(grandchild);form.MoveFolderHistory(-1);form.LoadFolder(root);
                    check("New folder navigation discards stale Forward history",!form.browserForward.Enabled);
                    form.LoadFolder(Path.Combine(root,"missing"));
                    check("Unavailable folder addresses preserve the current folder and photos",SameFolder(form.currentFolder,root)&&form.folderPhotos.Photos.Count==24&&form.browserAddress.Text==form.currentFolder);
                    var rootNode=form.folders.SelectedNode;rootNode.Expand();var childNode=rootNode.Nodes.Cast<TreeNode>().Single(n=>SameFolder(n.Tag as string,child));childNode.Expand();
                    var tree=form.folders;var tiles=form.folderPhotos;var photoList=tiles.Photos;
                    tiles.AutoScrollPosition=new Point(0,200);Point scroll=tiles.AutoScrollPosition;
                    form.Focus(visible[0]);form.Options.Paper="3.5x5";form.CurrentLayout=layout;form.BuildPrinting();form.pageIndex=2;form.UpdatePage();form.pageCopies.Value=0;
                    form.ReturnToBrowser();
                    check("Returning from printing reuses the browser and photo list without reimport",form.folders==tree&&form.folderPhotos==tiles&&Object.ReferenceEquals(photoList,tiles.Photos));
                    check("Returning preserves expanded folders and selected tree node",rootNode.IsExpanded&&childNode.IsExpanded&&form.folders.SelectedNode==rootNode);
                    check("Returning preserves photo scroll and browser focus",tiles.AutoScrollPosition==scroll&&form.FocusPhoto==visible[0]);
                    foreach(var photo in form.Selected)photo.Copies=0;form.RefreshSelection();
                    foreach(var photo in visible.Skip(3).Take(3)){photo.Copies=1;form.RefreshSelection();}
                    form.BuildPrinting();
                    check("A new photo selection opens page one instead of the previous page",form.pageIndex==0&&form.Pages.Count==3&&(form.Pages[0].Photos[0].Original??form.Pages[0].Photos[0])==visible[3]);
                    check("A new selection clears old zero-copy and manual page state",form.Pages.All(p=>p.Copies==1)&&form.pageCopyCounts.Count==0);
                    form.ReturnToBrowser();
                    string added=Path.Combine(root,"New Folder");Directory.CreateDirectory(added);form.RefreshExplorer();
                    check("Refresh discovers added folders without collapsing existing branches",rootNode.Nodes.Cast<TreeNode>().Any(n=>SameFolder(n.Tag as string,added))&&childNode.IsExpanded);
                    Directory.Delete(added);form.RefreshExplorer();
                    check("Refresh removes disappeared folders and keeps the current location",!rootNode.Nodes.Cast<TreeNode>().Any(n=>SameFolder(n.Tag as string,added))&&SameFolder(form.currentFolder,root));
                    form.LoadFolder(child);Directory.CreateDirectory(added);form.RefreshExplorer();
                    check("Refresh discovers sibling folders while a different folder is selected",rootNode.Nodes.Cast<TreeNode>().Any(n=>SameFolder(n.Tag as string,added))&&SameFolder(form.currentFolder,child)&&childNode.IsExpanded);
                    Directory.Delete(added);WaitUntil(()=>!rootNode.Nodes.Cast<TreeNode>().Any(n=>SameFolder(n.Tag as string,added)));
                    Directory.CreateDirectory(added);WaitUntil(()=>rootNode.Nodes.Cast<TreeNode>().Any(n=>SameFolder(n.Tag as string,added)));
                    check("Folder creation refreshes an unselected branch automatically",SameFolder(form.currentFolder,child)&&form.folders.SelectedNode==childNode&&childNode.IsExpanded);
                    string renamed=Path.Combine(root,"Renamed Folder");Directory.Move(added,renamed);
                    WaitUntil(()=>rootNode.Nodes.Cast<TreeNode>().Any(n=>SameFolder(n.Tag as string,renamed))&&!rootNode.Nodes.Cast<TreeNode>().Any(n=>SameFolder(n.Tag as string,added)));
                    check("Folder renames appear automatically without losing navigation",SameFolder(form.currentFolder,child));
                    Directory.Delete(renamed);WaitUntil(()=>!rootNode.Nodes.Cast<TreeNode>().Any(n=>SameFolder(n.Tag as string,renamed)));
                    form.LoadFolder(root);form.Focus(visible[0]);var selectedBeforeRefresh=form.Selected.ToArray();
                    string newPhoto=Path.Combine(root,"new-photo.png");File.Copy(fixture,newPhoto);
                    WaitUntil(()=>form.folderPhotos.Photos.Any(p=>p.FilePath==newPhoto));
                    check("New photos appear automatically and preserve focus and selection",form.FocusPhoto==visible[0]&&form.Selected.SequenceEqual(selectedBeforeRefresh));
                    string renamedPhoto=Path.Combine(root,"renamed-photo.png");File.Move(newPhoto,renamedPhoto);
                    WaitUntil(()=>form.folderPhotos.Photos.Any(p=>p.FilePath==renamedPhoto)&&form.folderPhotos.Photos.All(p=>p.FilePath!=newPhoto));
                    File.Delete(renamedPhoto);WaitUntil(()=>form.folderPhotos.Photos.Count==24);
                    check("Photo renames and deletions update the visible grid automatically",form.FocusPhoto==visible[0]&&form.Selected.SequenceEqual(selectedBeforeRefresh));
                    var incoming=Enumerable.Range(0,10).Select(i=>Path.Combine(root,"incoming-"+i+".png")).ToArray();
                    foreach(var path in incoming)File.Copy(fixture,path);
                    WaitUntil(()=>!form.importing&&form.folderPhotos.Photos.Count==34);
                    check("Automatic batch refresh preserves focus without opening a progress window",form.FocusPhoto==visible[0]&&form.Selected.SequenceEqual(selectedBeforeRefresh)&&!Application.OpenForms.Cast<Form>().Any(f=>f.Visible&&f.Text=="Loading Photos"));
                    foreach(var path in incoming)File.Delete(path);WaitUntil(()=>!form.importing&&form.folderPhotos.Photos.Count==24);
                    Directory.CreateDirectory(added);form.LoadFolder(added);Directory.Delete(added);
                    WaitUntil(()=>SameFolder(form.currentFolder,root));
                    check("Deleting the open folder returns to its available parent",SameFolder(form.folders.SelectedNode.Tag as string,root));
                    form.BuildPrinting();Directory.CreateDirectory(added);form.ReturnToBrowser();
                    WaitUntil(()=>rootNode.Nodes.Cast<TreeNode>().Any(n=>SameFolder(n.Tag as string,added)));
                    check("Folder changes during printing appear after returning to the browser",SameFolder(form.currentFolder,root));
                    Directory.Delete(added);WaitUntil(()=>!rootNode.Nodes.Cast<TreeNode>().Any(n=>SameFolder(n.Tag as string,added)));
                    form.ApplyDpi(144);form.BuildPrinting();form.ReturnToBrowser();
                    check("Retained browser navigation scales after DPI changes",form.folders.Width==form.Pixels(160)&&form.browserNavigation.Height==form.Pixels(26)&&form.browserBack.Width==form.Pixels(30)&&Math.Abs(form.browserAddress.Font.Size-18)<.1);
                    var keyMessage=new Message();form.ProcessCmdKey(ref keyMessage,Keys.Control|Keys.L);
                    check("Ctrl+L focuses and selects the folder address",form.ActiveControl==form.browserAddress&&form.browserAddress.SelectionLength==form.browserAddress.Text.Length);
                    var rightClick=typeof(TreeView).GetMethod("OnNodeMouseClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                    rightClick.Invoke(form.folders,new object[]{new TreeNodeMouseClickEventArgs(childNode,MouseButtons.Right,1,0,0)});
                    check("Folder context selection leaves the current photo folder unchanged",form.folders.SelectedNode==childNode&&SameFolder(form.currentFolder,root));
                    rightClick.Invoke(form.folders,new object[]{new TreeNodeMouseClickEventArgs(childNode,MouseButtons.Left,1,0,0)});
                    check("Left-clicking a context-selected folder opens it normally",SameFolder(form.currentFolder,child));form.LoadFolder(root);
                    form.LoadPhotoBatch(visible.Select(p=>p.FilePath).ToList(),false,0,false);form.BuildPrinting();WaitUntil(()=>!form.importing);form.ReturnToBrowser();
                    check("A batch finishing during printing updates the retained browser",form.folderPhotos.Photos.Count==24&&form.folderPhotos.Photos[0]==visible[23]&&form.FocusPhoto==visible[23]);
                    form.BuildPrinting();form.ImportFiles(new[]{grandchild},false);form.ReturnToBrowser();
                    check("Folders imported during printing synchronize the retained tree and address",SameFolder(form.folders.SelectedNode.Tag as string,grandchild)&&form.browserAddress.Text==grandchild&&form.folderPhotos.Photos.Count==0);form.LoadFolder(root);
                    form.BuildPrinting();form.browserThumbnailSize=170;form.browserOneClickSelection=false;form.ReturnToBrowser();
                    check("Returning applies browser preferences changed on the print screen",form.folderPhotos.TileSize==170&&!form.folderPhotos.OneClickSelection);
                    SaveSurface(form,Path.Combine(directory,"browser-navigation.png"));
                }
                var work=Screen.PrimaryScreen.WorkingArea;
                var desired=VisibleWindowBounds(new Rectangle(work.Left+30,work.Top+30,1000,700),work,new Size(800,600));
                using(var form=new MainForm(new[]{fixture},prefs))
                {
                    form.Opacity=0;form.ShowInTaskbar=false;form.Show();Application.DoEvents();form.WindowState=FormWindowState.Normal;form.Bounds=desired;form.SaveWindowPlacement();
                    desired=form.Bounds;
                }
                using(var form=new MainForm(new[]{fixture},prefs))
                {
                    form.Opacity=0;form.ShowInTaskbar=false;form.Show();Application.DoEvents();
                    check("Reopening restores normal window size and position",form.WindowState==FormWindowState.Normal&&form.Bounds==desired);
                    form.WindowState=FormWindowState.Maximized;Application.DoEvents();form.SaveWindowPlacement();
                }
                using(var form=new MainForm(new[]{fixture},prefs))
                {
                    form.Opacity=0;form.ShowInTaskbar=false;form.Show();Application.DoEvents();
                    check("Reopening restores a maximized window",form.WindowState==FormWindowState.Maximized);
                    check("Maximized restoration keeps the previous normal window bounds",form.RestoreBounds==desired);
                    form.WindowState=FormWindowState.Minimized;Application.DoEvents();form.SaveWindowPlacement();
                }
                using(var form=new MainForm(new[]{fixture},prefs))
                {
                    form.Opacity=0;form.ShowInTaskbar=false;form.Show();Application.DoEvents();
                    check("Closing while minimized retains the prior maximized state",form.WindowState==FormWindowState.Maximized);
                    form.WindowState=FormWindowState.Normal;form.WindowState=FormWindowState.Minimized;Application.DoEvents();form.SaveWindowPlacement();
                }
                using(var form=new MainForm(new[]{fixture},prefs))
                {
                    form.Opacity=0;form.ShowInTaskbar=false;form.Show();Application.DoEvents();
                    check("Minimized windows reopen normally rather than minimized",form.WindowState==FormWindowState.Normal);
                }
                var clamped=VisibleWindowBounds(new Rectangle(9000,-9000,4000,3000),new Rectangle(-1280,0,1280,720),new Size(800,600));
                check("Saved windows remain visible after monitor removal or resolution changes",new Rectangle(-1280,0,1280,720).Contains(clamped)&&clamped.Size==new Size(1280,720));
                File.WriteAllText(prefs+".window","broken JSON");
                using(var form=new MainForm(new[]{fixture},prefs))
                {
                    form.Opacity=0;form.ShowInTaskbar=false;form.Show();Application.DoEvents();
                    check("Corrupt window preferences fall back to a usable normal window",form.WindowState==FormWindowState.Normal&&form.Width>=800);
                }
            }
            catch(Exception error){checks.Add(new VerificationCheck{Name="Browser/window state integration",Passed=false,Detail=error.ToString()});}
        }
    }
}
