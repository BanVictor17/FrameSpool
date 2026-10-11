using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class MainForm : Form
    {
        public readonly PrintOptions Options = new PrintOptions();
        public readonly List<PhotoItem> AllPhotos = new List<PhotoItem>();
        public List<PhotoPage> Pages = new List<PhotoPage>();
        public PhotoLayout CurrentLayout;
        public PhotoItem FocusPhoto;
        int pageIndex;
        bool printingScreen, manualMode, updating;
        string currentFolder;
        List<PhotoItem> searchResults = new List<PhotoItem>();
        Panel body;
        EventHandler bodyLayout;
        MenuStrip menu;
        TreeView folders;
        TileView folderPhotos, selectedPhotos;
        Label fileInfo, printerInfo, pageLabel, totalLabel, selectedCount;
        SkinButton next, enlarge, remove, positioning, correction;
        PagePreview preview;
        ListBox layouts;
        NumericUpDown pageCopies;
        CheckBox dateInfo, quickPrint;
        Form manualPalette;
        TileView manualPhotos;
        RadioButton photoLayoutRadio, frameLayoutRadio;
        ComboBox frameCategory;
        readonly ToolTip tips = new ToolTip();
        readonly string settingsFile = Path.Combine(AppIdentity.DataDirectory, "preferences.json");
        readonly Dictionary<string, int> pageCopyCounts = new Dictionary<string,int>();
        CancellationTokenSource importCancellation;
        bool importing;
        int loadMinimumImageSize;
        string pendingQuickPrint;
        readonly List<PhotoItem> launchPhotos = new List<PhotoItem>();
        public MainForm(string[] files)
        {
            Text=AppIdentity.Name; Size=new Size(800,600); MinimumSize=new Size(800,600);
            StartPosition=FormStartPosition.CenterScreen; Font=Ui.Font(12); AutoScaleMode=AutoScaleMode.None;
            BackColor=Color.FromArgb(252,252,254);Icon=AppAssets.CreateIcon();
            try { if(File.Exists(settingsFile)){var saved=new JavaScriptSerializer { MaxJsonLength=4000000 }.Deserialize<PrintOptions>(File.ReadAllText(settingsFile)); CopyOptions(saved,Options);} } catch {}
            Options.MinimumImageSize=Math.Max(60,Math.Min(600,Options.MinimumImageSize));Options.Expansion=Math.Max(0,Math.Min(2,Options.Expansion));
            loadMinimumImageSize=Options.MinimumImageSize;
            if(!new[]{"yyyy.MM.dd","MM.dd.yyyy","dd.MM.yyyy","yyyy/MM/dd","MM/dd/yyyy","dd/MM/yyyy","yyyy-MM-dd","MM-dd-yyyy","dd-MM-yyyy"}.Contains(Options.DateFormat))Options.DateFormat="yyyy.MM.dd";
            if(String.IsNullOrEmpty(Options.Printer))try{Options.Printer=new PrinterSettings().PrinterName;}catch{Options.Printer="";}
            FrameSpool.LayoutEngine.LoadEmbeddedCatalog();
            foreach(var design in FrameStore.Load())FrameSpool.LayoutEngine.Catalog.Add(design.ToLayout());
            CurrentLayout=FrameSpool.LayoutEngine.ForPaper(Options.Paper).FirstOrDefault();if(CurrentLayout==null){Options.Paper="A4";CurrentLayout=FrameSpool.LayoutEngine.ForPaper(Options.Paper).FirstOrDefault();}
            menu=new MenuStrip { Font=Ui.Font(12), Padding=new Padding(0), Height=20, AutoSize=false, BackColor=SystemColors.Control, Renderer=new ToolStripSystemRenderer() };
            body=new Panel { Dock=DockStyle.Fill, BackColor=Color.FromArgb(252,252,254) };
            Controls.Add(body); Controls.Add(menu); MainMenuStrip=menu; menu.Dock=DockStyle.Top;
            LoadBrowserPreferences(); BuildBrowser(); BuildMenus();
            AllowDrop=true; DragEnter+=delegate(object s,DragEventArgs e){if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};
            DragDrop+=delegate(object s,DragEventArgs e){ImportFiles((string[])e.Data.GetData(DataFormats.FileDrop),true);};
            FormClosing+=delegate { if(importCancellation!=null)importCancellation.Cancel();SavePreferences(); if(manualPalette!=null)manualPalette.Close(); tips.Dispose(); };
            Shown+=delegate { if(!browserDoNotShowSplash&&Opacity>0){var splash=new StartupSplash(this);splash.FormClosed+=delegate{splash.Dispose();};splash.Show(this);} if(files.Length>0){currentFolder=Directory.Exists(files[0])?files[0]:Path.GetDirectoryName(files[0]); ImportFiles(files,false);if(!importing)launchPhotos.AddRange(folderPhotos.Photos);}else LoadFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)); };
        }
        static void CopyOptions(PrintOptions a,PrintOptions b) { if(a==null||b==null)return;foreach(var f in typeof(PrintOptions).GetFields())f.SetValue(b,f.GetValue(a)); }
        void SavePreferences() { try{WorkflowStorage.Save(settingsFile,Options);}catch{} }
        public List<PhotoItem> Selected { get { return AllPhotos.Where(p=>p.Copies>0).ToList(); } }
        void ClearBody() { if(bodyLayout!=null){body.Resize-=bodyLayout;bodyLayout=null;}body.Paint-=PaintBrowserFooter;body.Paint-=PaintPrintFooter;var old=body.Controls.Cast<Control>().ToArray();body.Controls.Clear();foreach(var c in old)c.Dispose(); }
        void BuildMenus()
        {
            menu.Items.Clear();
            var file=new ToolStripMenuItem("&File");
            if(!printingScreen)
            { AddMenu(file,"&Open Photos...",OpenPhotos,Keys.Control|Keys.O);AddMenu(file,"Open &Folder...",OpenFolder);AddMenu(file,"&Find Photos...",ShowSearch,Keys.Control|Keys.F);AddMenu(file,"S&how Results",delegate{ShowFolderPhotos(searchResults);}); }
            else { AddMenu(file,"&Print",Print,Keys.Control|Keys.P); AddMenu(file,"&Manual Print...",delegate{PrintWithDriver(false);}); AddMenu(file,"Export pages...",ExportPages);AddMenu(file,"&Close",ReturnToBrowser); }
            file.DropDownItems.Add(new ToolStripSeparator());AddMenu(file,"E&xit",Close);menu.Items.Add(file);
            var props=new ToolStripMenuItem("Prop&erties");AddMenu(props,"&Select All",ShowSelectAll,Keys.Control|Keys.A);if(printingScreen)AddMenu(props,"&Reset Manual Flow",delegate{RebuildPages(true);});menu.Items.Add(props);
            var view=new ToolStripMenuItem("&View");var thumb=new ToolStripMenuItem("&Thumbnail Size");foreach(int n in new[]{80,130,170,220}){int size=n;AddMenu(thumb,n==80?"Small":n==130?"Normal":n==170?"Large":"Largest",delegate{browserThumbnailSize=size;PersistBrowserPreferences();var tiles=printingScreen?manualPhotos:folderPhotos;if(tiles!=null&&!tiles.IsDisposed){tiles.TileSize=size;tiles.RefreshPhotos();}});}view.DropDownItems.Add(thumb);
            if(!printingScreen){var sort=new ToolStripMenuItem("&Sort By");foreach(string kind in new[]{"File Name","Photo Date","File Modification Date"}){string choice=kind;var order=new ToolStripMenuItem(kind);AddMenu(order,"Ascending",delegate{SetPhotoSort(choice,false);});AddMenu(order,"Descending",delegate{SetPhotoSort(choice,true);});sort.DropDownItems.Add(order);}view.DropDownItems.Add(sort);AddMenu(view,"&Enlargement...",delegate{ShowEnlargement(FocusPhoto);});AddMenu(view,"&Refresh",delegate{LoadFolder(currentFolder);},Keys.F5);}else AddMenu(view,"&Photos to Print",ShowManualPalette,Keys.Control|Keys.M);menu.Items.Add(view);
            var tools=new ToolStripMenuItem("&Tools");if(printingScreen)AddMenu(tools,"&Register Quick Print Settings...",ShowQuickPrint);AddMenu(tools,"&Preferences...",ShowPreferences);menu.Items.Add(tools);
            var help=new ToolStripMenuItem("&Help");AddMenu(help,"FrameSpool &User's Guide",delegate{ShowHelp(printingScreen?"Print Settings":"Select Photos");},Keys.F1);var tooltips=new ToolStripMenuItem("Show &ToolTips"){Checked=browserShowToolTips,CheckOnClick=true};tooltips.CheckedChanged+=delegate{browserShowToolTips=tips.Active=tooltips.Checked;PersistBrowserPreferences();};help.DropDownItems.Add(tooltips);var alerts=new ToolStripMenuItem("&Show Alerts"){Checked=browserShowAlerts,CheckOnClick=true};alerts.CheckedChanged+=delegate{browserShowAlerts=alerts.Checked;PersistBrowserPreferences();};help.DropDownItems.Add(alerts);AddMenu(help,"RAW Format Information...",ShowRawInfo);AddMenu(help,"&About FrameSpool...",ShowAbout);menu.Items.Add(help);
        }
        static void AddMenu(ToolStripMenuItem parent,string text,Action action,Keys shortcut=Keys.None) { var item=new ToolStripMenuItem(text);item.Click+=delegate{action();};if(shortcut!=Keys.None)item.ShortcutKeys=shortcut;parent.DropDownItems.Add(item); }
        void SetPhotoSort(string kind,bool descending){browserSort=kind;browserSortDescending=descending;PersistBrowserPreferences();if(folderPhotos!=null&&!folderPhotos.IsDisposed)ShowFolderPhotos(folderPhotos.Photos);}
        void BuildBrowser()
        {
            printingScreen=false;ClearBody();
            folders=new TreeView{Location=new Point(5,5),ShowLines=true,HideSelection=false,Font=Ui.Font(12),BorderStyle=BorderStyle.Fixed3D};
            var images=new ImageList{ImageSize=new Size(16,16),ColorDepth=ColorDepth.Depth32Bit};images.Images.Add(SystemIcons.WinLogo);using(var bmp=new Bitmap(16,16)){using(var g=Graphics.FromImage(bmp)){g.FillRectangle(Brushes.Goldenrod,1,4,14,10);g.FillRectangle(Brushes.Goldenrod,2,2,6,4);g.DrawRectangle(Pens.DarkGoldenrod,1,4,13,9);}images.Images.Add(bmp);var initializedHandle=images.Handle;}folders.ImageList=images;folders.Disposed+=delegate{images.Dispose();};
            AddFolderNode(folders.Nodes,"Desktop",Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),0);
            AddFolderNode(folders.Nodes,"Public",Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),1);
            var computer=folders.Nodes.Add("Computer");computer.ImageIndex=0;computer.SelectedImageIndex=0;
            foreach(var drive in DriveInfo.GetDrives())AddFolderNode(computer.Nodes,drive.Name,drive.Name,0);
            AddFolderNode(folders.Nodes,"Pictures",Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),1);
            AddFolderNode(folders.Nodes,"Documents",Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),1);
            string downloads=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");if(Directory.Exists(downloads))AddFolderNode(folders.Nodes,"Downloads",downloads,1);
            computer.Expand();
            folders.BeforeExpand+=delegate(object s,TreeViewCancelEventArgs e){if(e.Node.Nodes.Count==1&&e.Node.Nodes[0].Tag==null){e.Node.Nodes.Clear();try{foreach(var dir in Directory.EnumerateDirectories((string)e.Node.Tag).OrderBy(p=>Path.GetFileName(p),StringComparer.CurrentCultureIgnoreCase))if((File.GetAttributes(dir)&FileAttributes.Hidden)==0)AddFolderNode(e.Node.Nodes,Path.GetFileName(dir),dir,1);}catch{e.Node.Nodes.Add(new TreeNode("Folder unavailable"){ForeColor=Color.Gray});}}};
            folders.AfterSelect+=delegate(object s,TreeViewEventArgs e){var path=e.Node.Tag as string;if(path!=null)LoadFolder(path);};body.Controls.Add(folders);
            folderPhotos=new TileView{OneClickSelection=browserOneClickSelection,TileSize=browserThumbnailSize,BackColor=Color.FromArgb(221,221,221)};selectedPhotos=new TileView{SelectionList=true,BackColor=Color.FromArgb(221,221,221)};body.Controls.Add(folderPhotos);body.Controls.Add(selectedPhotos);
            folderPhotos.PhotoFocused+=Focus;selectedPhotos.PhotoFocused+=Focus;folderPhotos.PhotoEnlarged+=ShowEnlargement;selectedPhotos.PhotoEnlarged+=ShowEnlargement;
            folderPhotos.CopiesChanged+=delegate(PhotoItem p){if(Selected.Count>999){p.Copies=0;MessageBox.Show(this,"You can select up to 999 photos.","FrameSpool");}RefreshSelection();};
            selectedPhotos.CopiesChanged+=delegate{RefreshSelection();};
            var context=new ContextMenuStrip();context.Items.Add("Enlargement...",null,delegate{ShowEnlargement(FocusPhoto);});context.Items.Add("Remove",null,delegate{RemoveFocused();});context.Items.Add("Remove All",null,delegate{foreach(var p in Selected)p.Copies=0;RefreshSelection();});selectedPhotos.ContextMenuStrip=context;
            selectedCount=Ui.Label(body,"Photos to Print",0,10,150,18);
            fileInfo=Ui.Label(body,"File:\r\nSize:\r\nDate:",290,0,250,70);
            var all=Ui.Skin(body,"Select All","EPQImage/btn_1-1-02_u.bmp",5,0,198,41,ShowSelectAll);all.CaptionRectangle=new Rectangle(35,0,160,41);all.TextPixelSize=14;
            enlarge=Ui.Skin(body,"Enlarge","EPQImage/btn_1-1-03_u.bmp",5,0,198,41,delegate{ShowEnlargement(FocusPhoto);});enlarge.CaptionRectangle=new Rectangle(35,0,160,41);enlarge.TextPixelSize=14;
            remove=Ui.Skin(body,"Remove","EPQImage/btn_1-1-06_u.bmp",0,0,143,28,RemoveFocused);remove.CaptionRectangle=new Rectangle(20,0,120,27);
            next=Ui.Skin(body,"Next Step","EPQImage/btn_1-1-08_u.bmp",0,0,150,66,delegate{if(Selected.Count==0)return;if(quickPrint!=null&&quickPrint.Checked){RunQuickPrint();return;}BuildPrinting();BuildMenus();});next.CaptionRectangle=new Rectangle(0,47,150,16);next.TextPixelSize=14;
            quickPrint=Ui.Check(body,"Print Using Registered Settings",0,0,270,false,null);var registered=ReadQuickPrint();quickPrint.Visible=registered.A!=null||registered.B!=null;quickPrint.CheckedChanged+=delegate{next.Text=quickPrint.Checked?"Quick Print":"Next Step";};
            tips.Active=browserShowToolTips;tips.SetToolTip(all,"Set copies for every photo in this folder.");tips.SetToolTip(enlarge,"Show the focused photo and its metadata.");tips.SetToolTip(remove,"Remove the focused photo from Photos to Print.");tips.SetToolTip(next,"Choose the layout and print settings.");tips.SetToolTip(quickPrint,"Print using registered settings A or B.");tips.SetToolTip(folders,"Browse local folders and removable drives.");
            body.Paint+=PaintBrowserFooter;
            bodyLayout=delegate { if(printingScreen)return;int w=body.Width,h=body.Height;folders.Bounds=new Rectangle(5,5,160,h-95);folderPhotos.Bounds=new Rectangle(169,5,w-334,h-95);selectedPhotos.Bounds=new Rectangle(w-155,30,143,h-165);selectedCount.Bounds=new Rectangle(w-145,10,141,18);all.Top=h-88;enlarge.Top=h-44;remove.Location=new Point(w-155,h-128);next.Location=new Point(w-159,h-77);quickPrint.Location=new Point(w-288,h-88);fileInfo.Bounds=new Rectangle(290,h-66,Math.Max(50,w-590),60);};body.Resize+=bodyLayout;
            body.PerformLayout(); OnResize(EventArgs.Empty); LayoutBrowser();RefreshSelection();
        }
        void LayoutBrowser(){int w=body.Width,h=body.Height;folders.Bounds=new Rectangle(5,5,160,h-95);folderPhotos.Bounds=new Rectangle(169,5,w-334,h-95);selectedPhotos.Bounds=new Rectangle(w-155,30,143,h-165);selectedCount.Bounds=new Rectangle(w-145,10,141,18);body.Controls.OfType<SkinButton>().First(b=>b.Text=="Select All").Top=h-88;enlarge.Top=h-44;remove.Location=new Point(w-155,h-128);next.Location=new Point(w-159,h-77);quickPrint.Location=new Point(w-288,h-88);fileInfo.Bounds=new Rectangle(290,h-66,Math.Max(50,w-590),60);}
        void PaintBrowserFooter(object s,PaintEventArgs e){if(printingScreen)return;var bmp=Skin.GetImage("EPQImage/bmp_1-1-07.bmp");if(bmp!=null)e.Graphics.DrawImage(bmp,new Rectangle(210,body.Height-88,body.Width-210,93));e.Graphics.DrawLine(Pens.Silver,body.Width-160,3,body.Width-5,3);}
        void AddFolderNode(TreeNodeCollection nodes,string name,string path,int icon){if(String.IsNullOrEmpty(path))return;using(var shellIcon=FolderShellIcon.Read(path)){if(shellIcon!=null){folders.ImageList.Images.Add(shellIcon);icon=folders.ImageList.Images.Count-1;}}var node=new TreeNode(name){Tag=path,ImageIndex=icon,SelectedImageIndex=icon};node.Nodes.Add(new TreeNode(""));nodes.Add(node);}
        void LoadFolder(string path)
        { if(String.IsNullOrEmpty(path))return;if(!Directory.Exists(path)){ShowFolderPhotos(new List<PhotoItem>());ShowBrowserAlert("This folder is unavailable. Reconnect the drive or choose another folder.","Folder");return;}currentFolder=path;try{ImportFiles(new[]{path},false);}catch(Exception ex){ShowBrowserAlert(ex.Message,"Folder");} }
        void ImportFiles(string[] files,bool select)
        {
            if(importCancellation!=null){importCancellation.Cancel();importCancellation=null;importing=false;}
            var expanded=new List<string>();int invalid=0;bool limited=false;
            foreach(var entry in files??new string[0])try{if(Directory.Exists(entry)){currentFolder=entry;foreach(var path in Directory.EnumerateFiles(entry)){if(!PhotoEngine.CanDecode(path))continue;if(expanded.Count==999){limited=true;break;}expanded.Add(Path.GetFullPath(path));}}else if(PhotoEngine.CanDecode(entry)){if(expanded.Count==999){limited=true;break;}expanded.Add(Path.GetFullPath(entry));}}catch{invalid++;}
            expanded=expanded.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(expanded.Count>32){LoadPhotoBatch(expanded,select,invalid,limited);return;}
            var visible=new List<PhotoItem>();foreach(var path in expanded){var photo=AllPhotos.FirstOrDefault(p=>String.Equals(p.FilePath,path,StringComparison.OrdinalIgnoreCase));if(photo==null)try{photo=new PhotoItem(path);if(PhotoEngine.ValidateDimensions(photo.Width,photo.Height,loadMinimumImageSize)!=null){invalid++;continue;}AllPhotos.Add(photo);}catch{invalid++;continue;}visible.Add(photo);}
            FinishImport(visible,select,invalid,limited);
        }
        void ShowFolderPhotos(List<PhotoItem> photos){if(folderPhotos==null||folderPhotos.IsDisposed)return;Func<PhotoItem,string> key=browserSort=="Photo Date"?(Func<PhotoItem,string>)(p=>p.CaptureDate.ToString("O")):browserSort=="File Modification Date"?(Func<PhotoItem,string>)(p=>SafeModified(p.FilePath).ToString("O")):(p=>p.FileName);var ordered=(photos??new List<PhotoItem>()).OrderBy(key,StringComparer.CurrentCultureIgnoreCase);folderPhotos.Photos=(browserSortDescending?ordered.Reverse():ordered).ToList();folderPhotos.RefreshPhotos();if(folderPhotos.Photos.Count>0)Focus(folderPhotos.Photos[0]);else Focus(null);}
        void Focus(PhotoItem photo){FocusPhoto=photo;if(folderPhotos!=null&&!folderPhotos.IsDisposed){folderPhotos.FocusPhoto=photo;folderPhotos.Invalidate();}if(selectedPhotos!=null&&!selectedPhotos.IsDisposed){selectedPhotos.FocusPhoto=photo;selectedPhotos.Invalidate();}if(fileInfo!=null&&!fileInfo.IsDisposed)fileInfo.Text=photo==null?"File:\r\nSize:\r\nDate:":"File:      "+photo.FileName+"\r\nSize:     "+(photo.FileSize/1024).ToString("N0")+" KB\r\nDate:    "+photo.CaptureDate.ToString("yyyy.MM.dd HH:mm");if(preview!=null&&!preview.IsDisposed){preview.FocusPhoto=photo;preview.Invalidate();}}
        void RefreshSelection(){if(!printingScreen&&selectedPhotos!=null&&!selectedPhotos.IsDisposed){selectedPhotos.Photos=Selected;selectedPhotos.RefreshPhotos();folderPhotos.Invalidate();next.Enabled=Selected.Count>0;enlarge.Enabled=FocusPhoto!=null;remove.Enabled=FocusPhoto!=null&&FocusPhoto.Copies>0;selectedCount.Text="Photos to Print";}if(printingScreen)UpdatePage();}
        void RemoveFocused(){if(FocusPhoto==null)return;FocusPhoto.Copies=0;RefreshSelection();}
        void BuildPrinting()
        {
            bool preferredFrame=CurrentLayout!=null&&(CurrentLayout.Design!=null||CurrentLayout.Overlay!=null);
            printingScreen=true;ClearBody();
            if(CurrentLayout==null)CurrentLayout=FrameSpool.LayoutEngine.ForPaper(Options.Paper).FirstOrDefault();RebuildPages();
            var printer=Ui.Skin(body,"Printer Settings","Image/btn_2-1-01_u.bmp",10,10,200,44,ShowPrinterSettings);printer.CaptionRectangle=new Rectangle(35,0,165,44);
            printerInfo=Ui.Label(body,"",10,56,200,114);printerInfo.BorderStyle=BorderStyle.Fixed3D;printerInfo.BackColor=Color.White;
            Ui.Label(body,"Select Layout",10,178,200,16);
            var layoutGroup=new Panel{Bounds=new Rectangle(18,196,192,36)};body.Controls.Add(layoutGroup);
            photoLayoutRadio=new RadioButton{Text="Photo Layout",Bounds=new Rectangle(0,0,192,17),Checked=true};frameLayoutRadio=new RadioButton{Text="Frame",Bounds=new Rectangle(0,17,192,17)};layoutGroup.Controls.Add(photoLayoutRadio);layoutGroup.Controls.Add(frameLayoutRadio);
            frameCategory=Ui.Combo(body,new[]{"All","PhotoFrame","Greeting","Calender","Album","Other"},40,233,170,"All",delegate{if(frameLayoutRadio.Checked)FillLayouts(true); });frameCategory.Enabled=false;
            frameLayoutRadio.CheckedChanged+=delegate{frameCategory.Enabled=frameLayoutRadio.Checked;FillLayouts(frameLayoutRadio.Checked);};
            layouts=new ListBox{Location=new Point(10,259),Size=new Size(200,230),Font=Ui.Font(12),DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=55,IntegralHeight=false};layouts.DrawItem+=DrawLayoutItem;layouts.SelectedIndexChanged+=delegate{if(updating)return;CurrentLayout=layouts.SelectedItem as PhotoLayout;RebuildPages();UpdatePage();};body.Controls.Add(layouts);
            var frame=Ui.Skin(body,"Frame Menu","Image/btn_2-1-08_u.bmp",10,0,198,30,ShowFrameMenu);frame.CaptionRectangle=new Rectangle(30,0,168,30);frame.TextPixelSize=14;frame.TextBold=true;
            Ui.Label(body,"Photo Layout",232,10,148,16);
            var flowGroup=new Panel{Bounds=new Rectangle(240,28,120,34)};body.Controls.Add(flowGroup);
            var automatic=new RadioButton{Text="Automatic",Bounds=new Rectangle(0,0,120,17),Checked=!manualMode};var manual=new RadioButton{Text="Manual",Bounds=new Rectangle(0,16,120,17),Checked=manualMode};flowGroup.Controls.Add(automatic);flowGroup.Controls.Add(manual);
            manual.CheckedChanged+=delegate{manualMode=manual.Checked;preview.EnableManual=manualMode;if(manualMode)ShowManualPalette();else{if(manualPalette!=null)manualPalette.Close();RebuildPages(true);}};
            var fit=Ui.Check(body,"Fit to Window",0,10,150,true,delegate(bool value){if(preview!=null)preview.FitToWindow=value;});
            positioning=Ui.Skin(body,"Positioning","Image/btn_2-1-14_u.bmp",0,31,140,29,ShowPositioning);positioning.CaptionRectangle=new Rectangle(22,0,118,29);
            correction=Ui.Skin(body,"Image Correct","Image/btn_2-1-15_u.bmp",0,31,140,29,ShowCorrection);correction.CaptionRectangle=new Rectangle(22,0,118,29);
            var back=Ui.Skin(body,"Back","Image/btn_2-1-16_u.bmp",0,26,100,40,ReturnToBrowser);back.CaptionRectangle=new Rectangle(20,0,80,40);
            preview=new PagePreview{Options=Options,EnableManual=manualMode};preview.PhotoFocused+=Focus;preview.PhotoDropped+=AssignManualPhoto;preview.PhotoMoved+=delegate(int from,int to){if(Pages.Count==0||from==to||from>=Pages[pageIndex].Photos.Count)return;var page=Pages[pageIndex];var photo=page.Photos[from];while(page.Photos.Count<=to)page.Photos.Add(null);page.Photos[from]=page.Photos[to];page.Photos[to]=photo;UpdatePage();};preview.SlotCleared+=delegate(int index){if(Pages.Count>0&&index<Pages[pageIndex].Photos.Count){Pages[pageIndex].Photos[index]=null;Focus(null);UpdatePage();}};body.Controls.Add(preview);
            pageLabel=Ui.Label(body,"Page\r\n1/1",240,0,80,36);
            var previous=Ui.Skin(body,"","Image/btn_2-1-22_u.bmp",325,0,55,24,delegate{if(pageIndex>0){pageIndex--;UpdatePage();}});var following=Ui.Skin(body,"","Image/btn_2-1-23_u.bmp",390,0,55,24,delegate{if(pageIndex<Pages.Count-1){pageIndex++;UpdatePage();}});
            pageCopies=Ui.Number(body,480,0,51,0,99,1,delegate(int value){if(!updating&&Pages.Count>0){Pages[pageIndex].Copies=value;pageCopyCounts[pageIndex.ToString()]=value;UpdatePage();}});var pageWord=Ui.Label(body,"page",535,0,65,26);
            var number=Ui.Skin(body,"Number of Pages","Image/btn_2-1-27_u.bmp",0,0,148,29,ShowPageCounts);number.CaptionRectangle=new Rectangle(20,0,128,29);
            totalLabel=Ui.Label(body,"",0,0,193,32);
            dateInfo=Ui.Check(body,Options.InfoFileName?"File Name":"Date",0,0,193,Options.PrintInfo,delegate(bool value){Options.PrintInfo=value;preview.Invalidate();});
            var redeye=Ui.Button(body,"Fix Red-Eye All (Auto)",0,0,193,21,delegate{foreach(var p in Selected)p.Adjustments.AutoRedEye=true;UpdatePage();});
            var print=Ui.Skin(body,"Print","Image/btn_2-1-38_u.bmp",0,0,150,66,Print);print.CaptionRectangle=new Rectangle(0,48,150,16);print.TextPixelSize=14;
            tips.SetToolTip(printer,"Choose a Windows printer, paper and driver properties.");tips.SetToolTip(positioning,"Adjust cropping, scale, rotation and placement.");tips.SetToolTip(correction,"Preview and apply corrections to printed photos.");tips.SetToolTip(pageCopies,"Copies of this page, from 0 to 99. Zero skips the page.");tips.SetToolTip(dateInfo,"Print the date or file name in layouts that support captions.");tips.SetToolTip(number,"Set copies separately for each page, or apply one count to all.");tips.SetToolTip(print,"Print the selected pages with their image corrections.");
            body.Paint-=PaintBrowserFooter;body.Paint+=PaintPrintFooter;
            bodyLayout=delegate{if(!printingScreen||layouts.IsDisposed)return;int w=body.Width,h=body.Height;layouts.Height=h-309;frame.Top=h-40;fit.Left=w-418;positioning.Left=w-418;correction.Left=w-268;back.Left=w-118;preview.Bounds=new Rectangle(240,76,w-266,h-221);pageLabel.Top=h-140;previous.Top=h-135;following.Top=h-135;pageCopies.Top=h-135;pageWord.Top=h-135;number.Location=new Point(w-174,h-140);totalLabel.Location=new Point(w-363,h-80);dateInfo.Location=new Point(w-363,h-50);redeye.Location=new Point(w-363,h-25);print.Location=new Point(w-156,h-78);};body.Resize+=bodyLayout;
            if(preferredFrame)frameLayoutRadio.Checked=true;FillLayouts(preferredFrame);body.Size=new Size(body.Width,body.Height+1);body.Size=new Size(body.Width,body.Height-1);UpdatePage();UpdatePrinterSummary();
        }
        void PaintPrintFooter(object s,PaintEventArgs e){if(!printingScreen)return;var bmp=Skin.GetImage("Image/bmp_2-1-40.bmp");if(bmp!=null)e.Graphics.DrawImage(bmp,new Rectangle(220,body.Height-88,body.Width-220,93));e.Graphics.DrawLine(Pens.Silver,220,0,220,body.Height);}
        void FillLayouts(bool frames)
        {
            if(layouts==null)return;updating=true;layouts.Items.Clear();var items=FrameSpool.LayoutEngine.ForPaper(Options.Paper).Where(l=>l.Design==null&&l.Overlay==null).ToList();if(frames)items=FrameSpool.LayoutEngine.Catalog.Where(l=>l.Paper==Options.Paper&&(l.Design!=null||l.Overlay!=null)&&(frameCategory==null||frameCategory.Text=="All"||l.Design!=null&&l.Design.Category==frameCategory.Text)).ToList();foreach(var l in items)layouts.Items.Add(l);
            var matching=CurrentLayout==null?null:layouts.Items.Cast<PhotoLayout>().FirstOrDefault(l=>l.Id==CurrentLayout.Id);if(matching!=null){CurrentLayout=matching;layouts.SelectedItem=matching;}else if(layouts.Items.Count>0){layouts.SelectedIndex=0;CurrentLayout=layouts.Items[0] as PhotoLayout;}else CurrentLayout=null;
            updating=false;RebuildPages(true);UpdatePage();
        }
        void DrawLayoutItem(object sender,DrawItemEventArgs e)
        { if(e.Index<0)return;e.DrawBackground();var layout=(PhotoLayout)layouts.Items[e.Index];var box=new RectangleF(e.Bounds.X+4,e.Bounds.Y+4,43,43);var sheet=FrameSpool.LayoutEngine.PaperBounds(box,layout);if(layout.Design!=null)layout.Design.Draw(e.Graphics,sheet,null,false,null,true);else{e.Graphics.FillRectangle(Brushes.White,sheet);foreach(var f in layout.Frames){var r=new RectangleF(sheet.X+f.X*sheet.Width,sheet.Y+f.Y*sheet.Height,f.Width*sheet.Width,f.Height*sheet.Height);e.Graphics.FillRectangle(Brushes.LightSteelBlue,r);e.Graphics.DrawRectangle(Pens.SlateGray,r.X,r.Y,r.Width,r.Height);}}using(var font=Ui.Font(12))TextRenderer.DrawText(e.Graphics,layout.Name,font,new Rectangle(e.Bounds.X+53,e.Bounds.Y+4,e.Bounds.Width-55,e.Bounds.Height-8),e.ForeColor,TextFormatFlags.WordBreak);e.DrawFocusRectangle(); }
        void RebuildPages(){RebuildPages(false);}
        void RebuildPages(bool reset){if(manualMode&&!reset&&Pages.Count>0){if(preview!=null&&!preview.IsDisposed)UpdatePage();return;}CurrentLayout=FrameSpool.LayoutEngine.ResolveForPhotos(CurrentLayout,Selected);Pages=FrameSpool.LayoutEngine.Paginate(Selected,CurrentLayout);pageIndex=Math.Min(pageIndex,Math.Max(0,Pages.Count-1));for(int i=0;i<Pages.Count;i++){int count;if(pageCopyCounts.TryGetValue(i.ToString(),out count))Pages[i].Copies=count;}if(preview!=null&&!preview.IsDisposed)UpdatePage();}
        void UpdatePage()
        {
            if(!printingScreen||preview==null||preview.IsDisposed)return;pageIndex=Math.Max(0,Math.Min(pageIndex,Math.Max(0,Pages.Count-1)));preview.Page=Pages.Count>0?Pages[pageIndex]:null;CurrentLayout=FrameSpool.LayoutEngine.ResolveForPage(CurrentLayout,preview.Page);preview.LayoutDefinition=CurrentLayout;
            if(preview.Page==null||!preview.Page.Photos.Contains(FocusPhoto))FocusPhoto=preview.Page==null?null:preview.Page.Photos.FirstOrDefault(p=>p!=null);preview.FocusPhoto=FocusPhoto;preview.RefreshScale();if(pageLabel!=null&&!pageLabel.IsDisposed)pageLabel.Text="Page\r\n"+(Pages.Count==0?0:pageIndex+1)+"/"+Pages.Count;
            if(pageCopies!=null){updating=true;pageCopies.Value=Pages.Count==0?1:Pages[pageIndex].Copies;updating=false;}
            if(totalLabel!=null)totalLabel.Text="Total Prints:    "+Pages.Sum(p=>p.Copies)+"    prints";
            if(dateInfo!=null)dateInfo.Enabled=CurrentLayout!=null&&CurrentLayout.Info;
            if(positioning!=null)positioning.Enabled=FocusPhoto!=null;if(correction!=null)correction.Enabled=FocusPhoto!=null;
        }
        void UpdatePrinterSummary(){if(printerInfo==null||printerInfo.IsDisposed)return;printerInfo.Text=Options.Printer+"\r\n"+Options.Source+"\r\n"+(Options.Borderless?"Borderless":"Borders")+"\r\n"+Options.Paper+"\r\n"+Options.PaperType+"\r\n"+Options.Mode;}
        void ShowManualPalette()
        { if(manualPalette!=null&&!manualPalette.IsDisposed){manualPhotos.Photos=Selected;manualPhotos.RefreshPhotos();manualPalette.Show();return;}manualPalette=Ui.Dialog("Photos to Print",300,430);manualPalette.FormBorderStyle=FormBorderStyle.SizableToolWindow;manualPhotos=new TileView{Dock=DockStyle.Fill,Photos=Selected,OneClickSelection=false,TileSize=browserThumbnailSize};manualPhotos.PhotoFocused+=delegate(PhotoItem photo){var placed=Pages.Count==0?null:Pages[pageIndex].Photos.FirstOrDefault(p=>p!=null&&(p.Original??p)==photo);if(placed!=null)Focus(placed);};manualPhotos.AllowPhotoDrag=true;manualPalette.Controls.Add(manualPhotos);manualPalette.Show(this); }
        void ReturnToBrowser(){if(manualPalette!=null)manualPalette.Close();BuildBrowser();BuildMenus();LoadFolder(currentFolder);}
        void AssignManualPhoto(int index,string path){if(!manualMode)return;var photo=AllPhotos.FirstOrDefault(a=>String.Equals(a.FilePath,path,StringComparison.OrdinalIgnoreCase));if(photo==null){try{photo=new PhotoItem(path);if(PhotoEngine.ValidateDimensions(photo.Width,photo.Height,loadMinimumImageSize)!=null)return;photo.Copies=1;AllPhotos.Add(photo);}catch{return;}}if(Pages.Count==0)Pages.Add(new PhotoPage());var page=Pages[pageIndex];while(page.Photos.Count<=index)page.Photos.Add(null);var placement=photo.CreatePlacement();page.Photos[index]=placement;Focus(placement);UpdatePage();}
        void Print()
        {PrintWithDriver(true);}
        void PrintWithDriver(bool corrected)
        { PrintWithDriver(corrected,true); }
        void PrintWithDriver(bool corrected,bool chooseDriver)
        {
            if(Pages.Count==0||CurrentLayout==null)return;
            if(!CheckPageLimit())return;
            if(!Pages.Any(p=>p.Copies>0)){ShowBrowserAlert("Set at least one page copy before printing.","Printing");return;}
            if(Pages.SelectMany(p=>p.Photos).Any(p=>p!=null&&!File.Exists(p.FilePath))){MessageBox.Show(this,"A selected photo is unavailable. Reconnect its drive or remove the photo before printing.","Printing",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            using(var document=new PrintDocument())
            {
                document.DocumentName="FrameSpool";if(!String.IsNullOrEmpty(Options.Printer))document.PrinterSettings.PrinterName=Options.Printer;
                if(!document.PrinterSettings.IsValid){MessageBox.Show(this,"Install and select your printer's Windows driver, or export the pages to PNG.","Printing",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
                PrinterProfile.Apply(document,CurrentLayout,Options);
                using(var dialog=new PrintDialog{Document=document,UseEXDialog=true,AllowSomePages=true})
                {
                    document.PrinterSettings.MinimumPage=1;document.PrinterSettings.MaximumPage=Pages.Count;
                    if(chooseDriver&&dialog.ShowDialog(this)!=DialogResult.OK)return;
                    Options.Printer=document.PrinterSettings.PrinterName;UpdatePrinterSummary();
                    PrinterProfile.EnsurePageFormat(document,CurrentLayout,Options);
                    // Keep the selected driver's full mode, including private L8050
                    // media, quality and borderless fields from Properties.
                    DriverSettings.Capture(document,Options);
                    document.DefaultPageSettings.Margins=new Margins(0,0,0,0);
                    int start=document.PrinterSettings.PrintRange==PrintRange.SomePages?document.PrinterSettings.FromPage-1:0;int end=document.PrinterSettings.PrintRange==PrintRange.SomePages?document.PrinterSettings.ToPage:Pages.Count;int p=start,copy=0;
                    start=Math.Max(0,Math.Min(Pages.Count-1,start));end=Math.Max(start+1,Math.Min(Pages.Count,end));p=start;while(p<end&&Pages[p].Copies==0)p++;
                    if(p>=end){ShowBrowserAlert("The selected page range has no copies to print.","Printing");return;}
                    PhotoLayout printLayout=CurrentLayout;
                    document.QueryPageSettings+=delegate(object s,QueryPageSettingsEventArgs e)
                    {
                        if(p>=end)return;
                        printLayout=FrameSpool.LayoutEngine.ResolveForPage(CurrentLayout,Pages[p]);
                        if(CurrentLayout.Roll){PrinterProfile.EnsurePageFormat(document,printLayout,Options);e.PageSettings=(PageSettings)document.DefaultPageSettings.Clone();}
                    };
                    document.PrintPage+=delegate(object s,PrintPageEventArgs e)
                    {
                        if(p>=end){e.HasMorePages=false;return;}
                        PrinterProfile.DrawPrintedPage(e,printLayout,Pages[p],Options,corrected);
                        copy++;if(copy>=Pages[p].Copies){p++;copy=0;}while(p<end&&Pages[p].Copies==0)p++;e.HasMorePages=p<end;
                    };
                    try{document.Print();SavePreferences();ShowBrowserAlert("The print job was sent to the Windows printer queue. Check the queue for printer progress.","Printing");}catch(Exception ex){MessageBox.Show(this,ex.Message,"Printing",MessageBoxButtons.OK,MessageBoxIcon.Error);}
                }
            }
        }
        async void ExportPages()
        {
            if(Pages.Count==0||CurrentLayout==null||!CheckPageLimit())return;
            string destination;using(var dialog=new FolderBrowserDialog{Description="Choose a folder for print-ready PNG pages."}){if(dialog.ShowDialog(this)!=DialogResult.OK)return;destination=dialog.SelectedPath;}
            var outputPaths=Enumerable.Range(0,Pages.Count).Select(i=>Path.Combine(destination,"Photo-page-"+(i+1).ToString("000")+".png")).ToArray();bool replace=false;
            if(outputPaths.Any(File.Exists)){var result=MessageBox.Show(this,"Some output pages already exist. Replace them? Choose No to export only new pages.","Export pages",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);if(result==DialogResult.Cancel)return;replace=result==DialogResult.Yes;}
            var pages=Pages.Select(p=>new PhotoPage{Copies=p.Copies,Photos=p.Photos.Select(photo=>photo==null?null:photo.Clone()).ToList()}).ToList();var layout=CurrentLayout;var options=new PrintOptions();CopyOptions(Options,options);
            var cancellation=new CancellationTokenSource();var progress=Ui.Dialog("Export pages",370,118);progress.FormBorderStyle=FormBorderStyle.FixedToolWindow;progress.ShowInTaskbar=false;
            var label=Ui.Label(progress,"Rendering print-ready pages...",15,15,340,20);var bar=new ProgressBar{Bounds=new Rectangle(15,42,340,16),Maximum=pages.Count};progress.Controls.Add(bar);progress.CancelButton=Ui.Button(progress,"Cancel",265,79,90,23,delegate{cancellation.Cancel();});progress.FormClosing+=delegate{cancellation.Cancel();};
            body.Enabled=menu.Enabled=false;progress.Show(this);var reporter=new Progress<int>(value=>{if(!progress.IsDisposed){bar.Value=Math.Min(bar.Maximum,value);label.Text="Exported page "+value+" of "+pages.Count;}});bool cancelled=false;int exported=0;
            try{await Task.Run(delegate{for(int i=0;i<pages.Count;i++){cancellation.Token.ThrowIfCancellationRequested();if(!replace&&File.Exists(outputPaths[i])){((IProgress<int>)reporter).Report(i+1);continue;}using(var bitmap=FrameSpool.LayoutEngine.RenderPage(FrameSpool.LayoutEngine.ResolveForPage(layout,pages[i]),pages[i],options,300,true)){bitmap.Save(outputPaths[i],System.Drawing.Imaging.ImageFormat.Png);exported++;}((IProgress<int>)reporter).Report(i+1);}},cancellation.Token);}
            catch(OperationCanceledException){cancelled=true;}catch(Exception error){if(!IsDisposed)MessageBox.Show(this,error.Message,"Export pages",MessageBoxButtons.OK,MessageBoxIcon.Error);cancelled=true;}
            finally{if(!progress.IsDisposed){progress.Close();progress.Dispose();}cancellation.Dispose();if(!IsDisposed){body.Enabled=menu.Enabled=true;ShowBrowserAlert((cancelled?"Export stopped. ":"Export complete. ")+exported+" print-ready page(s) saved at 300 dpi.","Export pages");}}
        }
        bool CheckPageLimit(){if(manualMode||CurrentLayout==null)return true;int capacity=CurrentLayout.Images.Count==CurrentLayout.Frames.Count?CurrentLayout.Images.Select((item,i)=>String.IsNullOrEmpty(item.Group)?i.ToString():item.Group).Distinct().Count():CurrentLayout.Frames.Count;if(Selected.Sum(p=>p.Copies)<=capacity*999)return true;MessageBox.Show(this,"This selection needs more than 999 pages. Reduce the photo copies or choose a layout with more photos per page.","FrameSpool",MessageBoxButtons.OK,MessageBoxIcon.Information);return false;}
        void ShowFrameMenu()
        { var popup=new ContextMenuStrip();popup.Items.Add("Create/Edit Frame",null,delegate{ShowFrameEditor();});popup.Items.Add("Add Frame",null,delegate{AddFrame();});var export=popup.Items.Add("Export Selected Frame...",null,delegate{ExportFrame();});export.Enabled=CurrentLayout!=null&&CurrentLayout.Design!=null;popup.Items.Add("Move/Delete Frame",null,ShowFrameLibrary);popup.Closed+=delegate{popup.Dispose();};popup.Show(body,new Point(10,body.Height-40)); }
        void AddFrame()
        { using(var dialog=new OpenFileDialog{Filter="Frame packages and editable frames|*.fspframe;*.json;*.pif;*.fd2|Frame artwork|*.png;*.bmp;*.jpg;*.jpeg"})if(dialog.ShowDialog(this)==DialogResult.OK)try{FrameDesign design;string extension=Path.GetExtension(dialog.FileName).ToLowerInvariant();if(extension==".json"||extension==".fspframe"||extension==".pif"||extension==".fd2"){design=extension==".json"?FrameStore.Read(dialog.FileName):LegacyFrames.Import(dialog.FileName);design.Id=Guid.NewGuid().ToString("N");}else{var basis=CurrentLayout??FrameSpool.LayoutEngine.ForPaper(Options.Paper).First();design=new FrameDesign{Name=Path.GetFileNameWithoutExtension(dialog.FileName),Paper=Options.Paper,Width=basis.Width,Height=basis.Height};design.Elements.Add(new FrameElement{X=.05f,Y=.05f,Width=.9f,Height=.9f});design.Elements.Add(new FrameElement{Kind="Decoration",X=0,Y=0,Width=1,Height=1,ImagePath=dialog.FileName});}RegisterFrame(design);}catch(Exception error){MessageBox.Show(this,error.Message,"Add Frame");} }
        void ExportFrame()
        {if(CurrentLayout==null||CurrentLayout.Design==null)return;using(var dialog=new SaveFileDialog{Filter="FrameSpool portable frame package|*.fspframe",DefaultExt="fspframe",AddExtension=true,FileName=String.Join("_",CurrentLayout.Name.Split(Path.GetInvalidFileNameChars()))})if(dialog.ShowDialog(this)==DialogResult.OK)try{LegacyFrames.Export(CurrentLayout.Design,dialog.FileName);ShowBrowserAlert("The editable frame and its artwork were exported together.","Export Frame");}catch(Exception error){MessageBox.Show(this,error.Message,"Export Frame");}}
        void RegisterFrame(FrameDesign design){FrameStore.Save(design);var old=FrameSpool.LayoutEngine.Catalog.FirstOrDefault(l=>l.Id=="custom:"+design.Id);if(old!=null)FrameSpool.LayoutEngine.Catalog.Remove(old);CurrentLayout=design.ToLayout();FrameSpool.LayoutEngine.Catalog.Add(CurrentLayout);Options.Paper=design.Paper;frameLayoutRadio.Checked=true;frameCategory.SelectedItem="All";FillLayouts(true);UpdatePrinterSummary();}
        void ShowFrameEditor()
        {
            bool edit=CurrentLayout!=null&&CurrentLayout.Design!=null;
            using(var chooser=Ui.Dialog("Create/Edit Frame",300,155))
            {var create=new RadioButton{Text="Create New Frame",Bounds=new Rectangle(25,25,250,23),Checked=!edit};var existing=new RadioButton{Text="Edit Selected Frame",Bounds=new Rectangle(25,60,250,23),Checked=edit,Enabled=edit};chooser.Controls.Add(create);chooser.Controls.Add(existing);Ui.Button(chooser,"Next",110,112,80,25,delegate{edit=existing.Checked;chooser.DialogResult=DialogResult.OK;});var cancel=Ui.Button(chooser,"Cancel",202,112,80,25,delegate{chooser.DialogResult=DialogResult.Cancel;});chooser.CancelButton=cancel;if(chooser.ShowDialog(this)!=DialogResult.OK)return;}
            var basis=CurrentLayout??FrameSpool.LayoutEngine.ForPaper(Options.Paper).First();var design=edit?basis.Design.Clone():new FrameDesign{Paper=Options.Paper,Width=basis.Width,Height=basis.Height};if(!edit)design.Elements.Add(new FrameElement());
            using(var editor=new FrameEditor(design))if(editor.ShowDialog(this)==DialogResult.OK)try{RegisterFrame(editor.Design);}catch(Exception error){MessageBox.Show(this,error.Message,"Save Frame");}
        }
        void ShowFrameLibrary(object sender,EventArgs args)
        {using(var dialog=Ui.Dialog("Move/Delete Frame",400,338)){var list=new ListBox{Bounds=new Rectangle(12,12,370,236)};foreach(var layout in FrameSpool.LayoutEngine.Catalog.Where(l=>l.Design!=null))list.Items.Add(layout.Design);dialog.Controls.Add(list);Ui.Label(dialog,"Category",12,263,70,20);var category=Ui.Combo(dialog,new[]{"PhotoFrame","Greeting","Calender","Album","Other"},85,258,160,"Other",null);list.SelectedIndexChanged+=delegate{var design=list.SelectedItem as FrameDesign;if(design!=null)category.SelectedItem=design.Category;};Ui.Button(dialog,"Move",257,258,85,25,delegate{var design=list.SelectedItem as FrameDesign;if(design==null)return;design.Category=category.Text;FrameStore.Save(design);FillLayouts(true);});Ui.Button(dialog,"Delete",12,301,85,25,delegate{var design=list.SelectedItem as FrameDesign;if(design==null)return;if(MessageBox.Show(dialog,"Delete the selected frame?","Frame",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;FrameStore.Remove(design);var layout=FrameSpool.LayoutEngine.Catalog.FirstOrDefault(l=>l.Id=="custom:"+design.Id);FrameSpool.LayoutEngine.Catalog.Remove(layout);list.Items.Remove(design);CurrentLayout=null;FillLayouts(true);});Ui.Button(dialog,"Close",297,301,85,25,delegate{dialog.Close();});dialog.ShowDialog(this);}}
        void ShowAbout(){using(var f=Ui.Dialog("Version Information",470,150)){Ui.Label(f,"FrameSpool "+Application.ProductVersion,70,15,380,20);Ui.Label(f,"Photo layouts, corrections and printing.\r\nIndependent application using Windows drivers.",70,40,380,46);Ui.Button(f,"Licenses...",70,110,110,25,delegate{LicenseDialog.Show(f);});Ui.Button(f,"OK",350,110,100,25,delegate{f.Close();});f.ShowDialog(this);}}
        void ShowRawInfo(){MessageBox.Show(this,"Camera RAW files are developed with the bundled decoder. Supported cameras depend on the decoder's camera database. RAW controls include exposure, white balance, scene and a histogram. JPEG, TIFF, PNG, BMP and GIF images can also be loaded. Source files are never changed.","RAW Format Information",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        void ShowHelp(string topic){using(var f=Ui.Dialog("FrameSpool User's Guide",600,440)){var text=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Bounds=new Rectangle(15,15,570,360),Font=Ui.Font(14),Text="Select Photos\r\nChoose a folder in the tree, File → Open Photos, or drop files/folders onto the app. Click a photo to select it. Use + and − to set copies, or Select All. Click Next Step to continue. Arrow keys change the focused photo, Space toggles selection and Delete removes a selected photo.\r\n\r\nPrint Settings\r\nChoose Printer Settings and a paper size, then a layout. Use the printer's Properties button for its complete media, quality and borderless options. Positioning changes cropping, scale, rotation and placement. Image Correct previews the printed corrections; the main layout preview shows source colors. Zero copies skips a page. Page Up/Down change pages and Escape returns to Select Photos.\r\n\r\nManual\r\nSelect Manual and drag photos from Photos to Print into page frames. Repeat a photo in several openings; each opening keeps independent positioning. Drag a placed photo outside the page to remove it. Use View → Photos to Print to reopen the palette.\r\n\r\nQuick Print\r\nTools → Register Quick Print Settings saves two sets, A and B. The optional desktop shortcuts print dropped photos with their saved settings. The confirmation checkbox controls the Quick Print confirmation.\r\n\r\nFrames\r\nFrame Menu creates or edits frames and imports compatible legacy frames, artwork or .fspframe packages. Export Selected Frame includes the editable frame and its artwork for sharing.\r\n\r\nPrinting without a physical printer\r\nChoose Microsoft Print to PDF, or File → Export pages for 300 dpi PNG output. Large imports and exports have a Cancel button.\r\n\r\nPreferences\r\nOne-Click Selection and the splash screen are set in Select Photos. Print Settings preferences select date/file-name captions and the minimum image size. Help menu toggles tooltips and informational alerts.\r\n\r\nThe correction algorithms are independent implementations. Exact proprietary image processing and printer-specific color behavior have not been established."};f.Controls.Add(text);Ui.Button(f,"Close",485,390,100,28,delegate{f.Close();});f.ShowDialog(this);}}
    }
}
