using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FrameSpool
{
    public sealed class BrowserPreferences
    {
        public bool OneClickSelection = true;
        public bool DoNotShowSplash;
        public bool ShowToolTips = true;
        public bool ShowAlerts = true;
        public int ThumbnailSize = 130;
        public string Sort = "File Name";
        public bool SortDescending;
        public string QuickPrintSlot = "A";
    }

    public sealed class QuickPrintSet
    {
        public PrintOptions Settings;
        public string LayoutId;
        public bool Confirm = true;
        public bool DesktopShortcut;
    }

    public sealed class QuickPrintStore
    {
        public QuickPrintSet A;
        public QuickPrintSet B;
    }

    public partial class MainForm
    {
        bool browserOneClickSelection = true;
        bool browserDoNotShowSplash;
        bool browserShowToolTips = true, browserShowAlerts = true;
        int browserThumbnailSize = 130;
        string browserSort = "File Name";
        bool browserSortDescending;
        string browserQuickPrintSlot = "A";

        void LoadBrowserPreferences()
        {
            try
            {
                if (!File.Exists(settingsFile + ".browser")) return;
                BrowserPreferences saved = new JavaScriptSerializer().Deserialize<BrowserPreferences>(File.ReadAllText(settingsFile + ".browser"));
                if (saved == null) return;
                browserOneClickSelection = saved.OneClickSelection;
                browserDoNotShowSplash = saved.DoNotShowSplash;
                browserShowToolTips = saved.ShowToolTips; browserShowAlerts = saved.ShowAlerts;
                browserThumbnailSize = new[]{80,130,170,220}.Contains(saved.ThumbnailSize) ? saved.ThumbnailSize : 130;
                browserSort = new[]{"File Name","Photo Date","File Modification Date"}.Contains(saved.Sort) ? saved.Sort : "File Name";
                browserSortDescending = saved.SortDescending;
                browserQuickPrintSlot = saved.QuickPrintSlot == "B" ? "B" : "A";
            }
            catch { }
        }

        void PersistBrowserPreferences()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
                BrowserPreferences saved = new BrowserPreferences {
                    OneClickSelection = browserOneClickSelection,
                    DoNotShowSplash = browserDoNotShowSplash,
                    ShowToolTips = browserShowToolTips, ShowAlerts = browserShowAlerts,
                    ThumbnailSize = browserThumbnailSize, Sort=browserSort, SortDescending=browserSortDescending,
                    QuickPrintSlot = browserQuickPrintSlot };
                WorkflowStorage.Save(settingsFile + ".browser", saved);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save preferences.\r\n" + ex.Message, "Preferences", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        void ShowEnlargement(PhotoItem initial)
        {
            if (initial == null) return;
            List<PhotoItem> candidates = folderPhotos != null && !folderPhotos.IsDisposed && folderPhotos.Photos.Contains(initial)
                ? new List<PhotoItem>(folderPhotos.Photos) : Selected;
            if (!candidates.Contains(initial)) candidates.Add(initial);
            int index = candidates.IndexOf(initial);
            using (Form dialog = Ui.Dialog("Enlargement", 800, 600))
            {
                PhotoItem current = initial;
                PhotoItem viewPhoto = current.Clone();
                BrowserPhotoView canvas = new BrowserPhotoView { Bounds = new Rectangle(12, 42, 531, 449), FitToWindow = true };
                dialog.Controls.Add(canvas);
                Ui.Check(dialog, "Fit to Window", 20, 15, 190, true, delegate(bool value) { canvas.FitToWindow = value; });
                SkinButton magnify = Ui.Skin(dialog, "", "EPQImage/btn_1-2-01_u.bmp", 469, 7, 33, 25, delegate { canvas.ZoomBy(1.25f); });
                SkinButton reduce = Ui.Skin(dialog, "", "EPQImage/btn_1-2-02_u.bmp", 510, 7, 33, 25, delegate { canvas.ZoomBy(.8f); });
                Ui.Label(dialog, "Rotate", 560, 58, 200, 16);
                Ui.Skin(dialog, "", "EPQImage/btn_1-2-03_u.bmp", 605, 80, 37, 20, delegate {
                    viewPhoto.Rotation--; canvas.SetImage(PhotoEngine.RenderImage(viewPhoto, false)); });
                Ui.Skin(dialog, "", "EPQImage/btn_1-2-04_u.bmp", 650, 80, 37, 20, delegate {
                    viewPhoto.Rotation++; canvas.SetImage(PhotoEngine.RenderImage(viewPhoto, false)); });
                Label file = Ui.Label(dialog, current.FileName, 29, 535, 480, 28);
                Label fileHeading = Ui.Label(dialog, "File Information", 555, 119, 230, 19);
                fileHeading.Font = new Font("Arial", 12, FontStyle.Bold, GraphicsUnit.Pixel);
                Label information = Ui.Label(dialog, "", 555, 140, 230, 50);
                Label photoHeading = Ui.Label(dialog, "Photo Information", 555, 209, 230, 19);
                photoHeading.Font = new Font("Arial", 12, FontStyle.Bold, GraphicsUnit.Pixel);
                Label photoInformation = Ui.Label(dialog, "", 555, 230, 232, 135);
                bool changing = false;
                NumericUpDown copies = null;
                copies = Ui.Number(dialog, 220, 510, 60, 0, 99, current.Copies, delegate(int value) {
                    if (changing) return;
                    if (current.Copies == 0 && value > 0 && Selected.Count >= 999)
                    {
                        changing = true; copies.Value = 0; changing = false;
                        MessageBox.Show(dialog, "The maximum number of readable image files is 999.", "Photos to Print");
                        return;
                    }
                    current.Copies = value;
                    RefreshSelection();
                });
                // Original minus/plus artwork uses horizontal arrows beside the count.
                Ui.Skin(dialog, "", "EPQImage/btn_1-2-28_u.bmp", 170, 510, 42, 19, delegate { if (copies.Value > 0) copies.Value--; });
                Ui.Skin(dialog, "", "EPQImage/btn_1-2-27_u.bmp", 288, 510, 43, 19, delegate { if (copies.Value < 99) copies.Value++; });
                Action reload = delegate {
                    current = candidates[index];
                    viewPhoto = current.Clone();
                    canvas.SetImage(PhotoEngine.RenderImage(viewPhoto, false));
                    file.Text = current.FileName;
                    information.Text = "- File Type:       " + MetadataValue(current, "File Type") + "\r\n- File Size:        " + (current.FileSize / 1024.0).ToString("N0") + " KB\r\n- Photo Size:     " + current.Width + " x " + current.Height;
                    photoInformation.Text = "- Date/Time:       " + current.CaptureDate.ToString("yyyy.MM.dd HH:mm:ss") +
                        "\r\n- Camera:          " + MetadataValue(current, "Camera") +
                        "\r\n- EXP Time:      " + MetadataValue(current, "EXP Time") +
                        "\r\n- F Number:      " + MetadataValue(current, "F Number") +
                        "\r\n- ISO Sensitivity: " + MetadataValue(current, "ISO Sensitivity") +
                        "\r\n- Flash:             " + MetadataValue(current, "Flash") +
                        "\r\n- Exif/DCF Color Space: " + MetadataValue(current, "Exif/DCF Color Space") +
                        "\r\n- Mono. Info:      " + MetadataValue(current, "Mono. Info");
                    changing = true; copies.Value = current.Copies; changing = false;
                    Focus(current);
                };
                Ui.Button(dialog, "Previous", 335, 559, 100, 21, delegate { if (index > 0) { index--; reload(); } });
                Ui.Button(dialog, "Next", 445, 559, 100, 21, delegate { if (index + 1 < candidates.Count) { index++; reload(); } });
                Ui.Button(dialog, "Help", 29, 559, 94, 21, delegate { ShowHelp("Enlargement"); });
                Button close = Ui.Button(dialog, "Close", 684, 569, 105, 21, delegate { dialog.Close(); });
                dialog.CancelButton = close;
                canvas.ViewChanged += delegate { magnify.Enabled = reduce.Enabled = !canvas.FitToWindow; };
                magnify.Enabled = reduce.Enabled = false;
                try { reload(); dialog.ShowDialog(this); }
                catch (Exception ex) { MessageBox.Show(this, "Cannot display the photo.\r\n" + ex.Message, "Enlargement", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            }
        }

        static string MetadataValue(PhotoItem photo, string key)
        {
            string value;
            return photo.Metadata != null && photo.Metadata.TryGetValue(key, out value) && !String.IsNullOrEmpty(value) ? value : "-";
        }

        void ShowSelectAll()
        {
            List<PhotoItem> targets = folderPhotos != null && !folderPhotos.IsDisposed ? new List<PhotoItem>(folderPhotos.Photos) : Selected;
            if (targets.Count == 0) return;
            using (Form dialog = Ui.Dialog("Select All", 207, 330))
            {
                Ui.Button(dialog, "Help", 100, 12, 95, 21, delegate { ShowHelp("Select All"); });
                Ui.Label(dialog, "Specify the same number of copies for all photos in the selected folder.", 20, 60, 167, 85);
                NumericUpDown count = Ui.Number(dialog, 34, 182, 60, 0, 99, 1, null);
                count.Height = 31;
                Ui.Skin(dialog, "", "EPQImage/btn_1-3-04_u.bmp", 109, 160, 58, 30, delegate { if (count.Value < 99) count.Value++; });
                Ui.Skin(dialog, "", "EPQImage/btn_1-3-05_u.bmp", 109, 200, 58, 30, delegate { if (count.Value > 0) count.Value--; });
                Button ok = Ui.Button(dialog, "OK", 12, 297, 85, 21, delegate {
                    int value = (int)count.Value;
                    int existingOutside = AllPhotos.Count(p => p.Copies > 0 && !targets.Contains(p));
                    int allowed = 999 - existingOutside, assigned = 0, skipped = 0;
                    // Keep already-selected targets before assigning new targets at the file limit.
                    foreach (PhotoItem photo in targets.OrderByDescending(p => p.Copies > 0))
                    {
                        if (value == 0) photo.Copies = 0;
                        else if (assigned < allowed) { photo.Copies = value; assigned++; }
                        else { photo.Copies = 0; skipped++; }
                    }
                    RefreshSelection();
                    if (printingScreen) RebuildPages();
                    dialog.Close();
                    if (skipped > 0) MessageBox.Show(this, "The maximum number of readable image files is 999. " + skipped + " photo(s) were not added.", "Select All");
                });
                Button cancel = Ui.Button(dialog, "Cancel", 104, 297, 85, 21, delegate { dialog.Close(); });
                dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                dialog.ShowDialog(this);
            }
        }

        sealed class SearchCriteria
        {
            public string Root, Name;
            public DateTime? ModifiedFrom, ModifiedTo, CaptureFrom, CaptureTo;
            public int Minimum;
        }

        sealed class SearchOutcome
        {
            public List<PhotoItem> Photos = new List<PhotoItem>();
            public int Scanned, Skipped;
            public bool Cancelled, Limited;
        }

        static TextBox BrowserText(Control parent, int x, int y, int width)
        {
            TextBox text = new TextBox { Bounds = new Rectangle(x, y, width, 21), Font = Ui.Font(12) };
            parent.Controls.Add(text);
            return text;
        }

        static DateTime? SearchDate(string date, string time, bool end)
        {
            date = date.Trim(); time = time.Trim();
            if (date.Length == 0)
            {
                if (time.Length > 0) throw new ArgumentException("You cannot specify only the time.");
                return null;
            }
            DateTime parsed;
            string[] formats = new string[] { "yyyy.MM.dd", "yyyy/MM/dd", "yyyy-MM-dd" };
            if (!DateTime.TryParseExact(date, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                throw new ArgumentException("The specified date contains an error. Use YYYY.MM.DD.");
            if (time.Length == 0) return end ? parsed.Date.AddDays(1).AddTicks(-1) : parsed.Date;
            DateTime clock;
            if (!DateTime.TryParseExact(time, new string[] { "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out clock))
                throw new ArgumentException("The specified time contains an error. Use HH:MM.");
            return parsed.Date.Add(clock.TimeOfDay);
        }

        void ShowSearch()
        {
            string root = currentFolder;
            if (String.IsNullOrEmpty(root) || !Directory.Exists(root))
            { MessageBox.Show(this, "Select a folder before searching.", "Search for Photos"); return; }
            using (Form dialog = Ui.Dialog("Search for Photos", 540, 320))
            {
                Ui.Label(dialog, "Search in:", 12, 14, 90, 18);
                Ui.Label(dialog, root, 105, 12, 420, 21);
                Ui.Label(dialog, "File Name:", 12, 50, 90, 18);
                TextBox name = BrowserText(dialog, 105, 48, 420);
                Ui.Label(dialog, "Modified date:", 12, 86, 90, 32);
                TextBox modifiedFrom = BrowserText(dialog, 105, 84, 105);
                Ui.Label(dialog, "to", 215, 86, 23, 18);
                TextBox modifiedTo = BrowserText(dialog, 243, 84, 105);
                Ui.Label(dialog, "2003.10.01", 105, 108, 105, 15);
                Ui.Label(dialog, "Date/Time:", 12, 135, 90, 18);
                TextBox captureFrom = BrowserText(dialog, 105, 133, 105);
                TextBox timeFrom = BrowserText(dialog, 215, 133, 75);
                Ui.Label(dialog, "to", 296, 135, 23, 18);
                TextBox captureTo = BrowserText(dialog, 325, 133, 105);
                TextBox timeTo = BrowserText(dialog, 435, 133, 80);
                Ui.Label(dialog, "2003.10.01", 105, 157, 105, 15);
                Ui.Label(dialog, "12:00", 215, 157, 75, 15);
                Label status = Ui.Label(dialog, "", 105, 177, 420, 49);
                bool running = false;
                CancellationTokenSource cancellation = null;
                Button stop = Ui.Button(dialog, "Cancel", 341, 257, 88, 21, delegate { if (cancellation != null) cancellation.Cancel(); });
                stop.Enabled = false;
                Button search = Ui.Button(dialog, "Search", 438, 257, 90, 21, delegate { });
                TextBox[] inputs = new TextBox[] { name, modifiedFrom, modifiedTo, captureFrom, timeFrom, captureTo, timeTo };
                search.Click += async delegate {
                    if (running) return;
                    SearchCriteria criteria;
                    try
                    {
                        criteria = new SearchCriteria { Root = root, Name = name.Text.Trim(), Minimum = loadMinimumImageSize,
                            ModifiedFrom = SearchDate(modifiedFrom.Text, "", false), ModifiedTo = SearchDate(modifiedTo.Text, "", true),
                            CaptureFrom = SearchDate(captureFrom.Text, timeFrom.Text, false), CaptureTo = SearchDate(captureTo.Text, timeTo.Text, true) };
                        if (criteria.Name.Length == 0 && !criteria.ModifiedFrom.HasValue && !criteria.ModifiedTo.HasValue && !criteria.CaptureFrom.HasValue && !criteria.CaptureTo.HasValue)
                            throw new ArgumentException("No file name, start date, or end date is indicated. Indicate at least one.");
                        if (criteria.ModifiedFrom > criteria.ModifiedTo || criteria.CaptureFrom > criteria.CaptureTo)
                            throw new ArgumentException("The start date and time is later than the end date and time. Enter the information again.");
                    }
                    catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Search for Photos"); return; }
                    cancellation = new CancellationTokenSource();
                    CancellationToken token = cancellation.Token;
                    running = true; search.Enabled = false; stop.Enabled = true;
                    foreach (TextBox input in inputs) input.Enabled = false;
                    status.Text = "Searching...";
                    Dictionary<string, PhotoItem> existing = AllPhotos.GroupBy(p => p.FilePath, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                    Progress<string> progress = new Progress<string>(delegate(string value) { if (!dialog.IsDisposed && !status.IsDisposed) status.Text = value; });
                    try
                    {
                        SearchOutcome result = await Task.Run(delegate { return FindBrowserPhotos(criteria, existing, token, progress); });
                        if (dialog.IsDisposed || IsDisposed) return;
                        foreach (PhotoItem photo in result.Photos) if (!existing.ContainsKey(photo.FilePath)) AllPhotos.Add(photo);
                        searchResults = result.Photos;
                        ShowFolderPhotos(searchResults);
                        status.Text = (result.Cancelled ? "Search cancelled. " : "Search complete. ") + result.Photos.Count + " photo(s) found." +
                            (result.Limited ? "\r\nSearch limit reached (999 matches, 50,000 files or 10,000 folders)." : "") +
                            (result.Skipped > 0 ? "\r\n" + result.Skipped + " file(s)/folder(s) could not be read." : "");
                    }
                    catch (Exception ex) { if (!dialog.IsDisposed) status.Text = "Could not complete the search. " + ex.Message; }
                    finally
                    {
                        running = false;
                        if (cancellation != null) { cancellation.Dispose(); cancellation = null; }
                        if (!dialog.IsDisposed)
                        { search.Enabled = true; stop.Enabled = false; foreach (TextBox input in inputs) input.Enabled = true; }
                    }
                };
                Button close = Ui.Button(dialog, "Close", 438, 287, 90, 21, delegate { if (cancellation != null) cancellation.Cancel(); dialog.Close(); });
                Ui.Button(dialog, "Help", 12, 287, 90, 21, delegate { ShowHelp("Search for Photos"); });
                dialog.CancelButton = close; dialog.AcceptButton = search;
                dialog.FormClosing += delegate { if (cancellation != null) cancellation.Cancel(); };
                dialog.ShowDialog(this);
            }
        }

        static SearchOutcome FindBrowserPhotos(SearchCriteria criteria, Dictionary<string, PhotoItem> existing, CancellationToken token, IProgress<string> progress)
        {
            SearchOutcome outcome = new SearchOutcome();
            Stack<string> pending = new Stack<string>(); pending.Push(criteria.Root);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int directories = 0;
            while (pending.Count > 0 && !token.IsCancellationRequested)
            {
                string directory = pending.Pop();
                if (!seen.Add(directory)) continue;
                if (++directories > 10000) { outcome.Limited = true; break; }
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                    foreach (string path in Directory.EnumerateFiles(directory))
                    {
                        if (token.IsCancellationRequested) break;
                        if (++outcome.Scanned > 50000) { outcome.Limited = true; break; }
                        if (outcome.Scanned % 25 == 0) progress.Report("Searching... " + outcome.Scanned + " files, " + outcome.Photos.Count + " matches\r\n" + Path.GetFileName(path));
                        if (!PhotoEngine.CanDecode(path)) continue;
                        if (criteria.Name.Length > 0 && Path.GetFileName(path).IndexOf(criteria.Name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        try
                        {
                            FileInfo file = new FileInfo(path);
                            if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            if (criteria.ModifiedFrom.HasValue && file.LastWriteTime < criteria.ModifiedFrom.Value) continue;
                            if (criteria.ModifiedTo.HasValue && file.LastWriteTime > criteria.ModifiedTo.Value) continue;
                            PhotoItem photo;
                            if (!existing.TryGetValue(Path.GetFullPath(path), out photo)) photo = new PhotoItem(path);
                            if (PhotoEngine.ValidateDimensions(photo.Width, photo.Height, criteria.Minimum) != null) continue;
                            if (criteria.CaptureFrom.HasValue && photo.CaptureDate < criteria.CaptureFrom.Value) continue;
                            if (criteria.CaptureTo.HasValue && photo.CaptureDate > criteria.CaptureTo.Value) continue;
                            outcome.Photos.Add(photo);
                            if (outcome.Photos.Count >= 999) { outcome.Limited = true; break; }
                        }
                        catch { outcome.Skipped++; }
                    }
                    if (outcome.Limited) break;
                    foreach (string child in Directory.EnumerateDirectories(directory))
                    {
                        if (token.IsCancellationRequested) break;
                        if (pending.Count + directories >= 10000) { outcome.Limited = true; break; }
                        try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child); }
                        catch { outcome.Skipped++; }
                    }
                }
                catch { outcome.Skipped++; }
            }
            outcome.Cancelled = token.IsCancellationRequested;
            return outcome;
        }

        void ShowPreferences()
        {
            if (!printingScreen)
            {
                using (Form dialog = Ui.Dialog("Preferences", 359, 165))
                {
                    Ui.Button(dialog, "Help", 252, 12, 95, 21, delegate { ShowHelp("Preferences"); });
                    CheckBox oneClick = Ui.Check(dialog, "One-Click Selection", 32, 35, 228, browserOneClickSelection, null);
                    CheckBox splash = Ui.Check(dialog, "Do Not Show Splash Screen.", 32, 60, 250, browserDoNotShowSplash, null);
                    Button ok = Ui.Button(dialog, "OK", 169, 132, 85, 21, delegate {
                        browserOneClickSelection = oneClick.Checked;
                        browserDoNotShowSplash = splash.Checked;
                        if (folderPhotos != null && !folderPhotos.IsDisposed)folderPhotos.OneClickSelection=browserOneClickSelection;
                        PersistBrowserPreferences(); dialog.Close();
                    });
                    Button cancel = Ui.Button(dialog, "Cancel", 262, 132, 85, 21, delegate { dialog.Close(); });
                    dialog.AcceptButton = ok; dialog.CancelButton = cancel; dialog.ShowDialog(this);
                }
                return;
            }
            using (Form dialog = Ui.Dialog("Preferences", 525, 447))
            {
                Ui.Button(dialog, "Help", 432, 14, 80, 21, delegate { ShowHelp("Preferences"); });
                GroupBox info = new GroupBox { Text = "Display Contents by Info Layout", Bounds = new Rectangle(15, 45, 497, 110) };
                dialog.Controls.Add(info);
                RadioButton date = new RadioButton { Text = "Date and Time", Bounds = new Rectangle(18, 30, 160, 18), Checked = !Options.InfoFileName };
                RadioButton file = new RadioButton { Text = "File Name", Bounds = new Rectangle(18, 80, 160, 18), Checked = Options.InfoFileName };
                info.Controls.Add(date); info.Controls.Add(file);
                string[] displayFormats = new string[] { "YYYY.MM.DD", "MM.DD.YYYY", "DD.MM.YYYY", "YYYY/MM/DD", "MM/DD/YYYY", "DD/MM/YYYY", "YYYY-MM-DD", "MM-DD-YYYY", "DD-MM-YYYY" };
                string[] formats = new string[] { "yyyy.MM.dd", "MM.dd.yyyy", "dd.MM.yyyy", "yyyy/MM/dd", "MM/dd/yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "MM-dd-yyyy", "dd-MM-yyyy" };
                int formatIndex = Array.IndexOf(formats, Options.DateFormat); if (formatIndex < 0) formatIndex = 0;
                ComboBox format = Ui.Combo(info, displayFormats, 18, 49, 160, displayFormats[formatIndex], null);
                CheckBox time = Ui.Check(info, "Display time", 199, 45, 230, Options.ShowTime, null);
                CheckBox seconds = Ui.Check(info, "Display time with seconds", 218, 70, 255, Options.ShowSeconds, null);
                Action enable = delegate { format.Enabled = time.Enabled = date.Checked; seconds.Enabled = date.Checked && time.Checked; };
                date.CheckedChanged += delegate { enable(); }; time.CheckedChanged += delegate { enable(); }; enable();
                GroupBox restart = new GroupBox { Text = "Changes will take effect after restarting.", Bounds = new Rectangle(15, 170, 497, 197) };
                dialog.Controls.Add(restart);
                Ui.Label(restart, "Minimum Loadable Image Dimension", 17, 32, 360, 18);
                NumericUpDown minimum = Ui.Number(restart, 38, 54, 53, 60, 600, Options.MinimumImageSize, null);
                Ui.Label(restart, "pixels", 100, 58, 150, 18);
                Ui.Label(restart, "Specify the minimum loadable image dimension as\r\n60 to 600 pixels portrait or landscape.", 38, 83, 360, 44);
                Ui.Label(restart, "RAW Development", 19, 132, 360, 18);
                Ui.Label(restart, "Camera RAW decoding is included. Exposure and white balance are in Image Correct > RAW Development.", 38, 155, 425, 30);
                Button ok = Ui.Button(dialog, "OK", 335, 379, 80, 21, delegate {
                    Options.InfoFileName = file.Checked; Options.DateFormat = formats[Math.Max(0, format.SelectedIndex)];
                    Options.ShowTime = time.Checked; Options.ShowSeconds = seconds.Checked;
                    Options.MinimumImageSize = (int)minimum.Value;
                    SavePreferences();
                    if (dateInfo != null && !dateInfo.IsDisposed) dateInfo.Text = Options.InfoFileName ? "File Name" : "Date";
                    UpdatePage(); dialog.Close();
                });
                Button cancel = Ui.Button(dialog, "Cancel", 432, 379, 80, 21, delegate { dialog.Close(); });
                dialog.AcceptButton = ok; dialog.CancelButton = cancel; dialog.ShowDialog(this);
            }
        }

        QuickPrintStore ReadQuickPrint()
        {
            try
            {
                if (File.Exists(settingsFile + ".quick"))
                    return new JavaScriptSerializer().Deserialize<QuickPrintStore>(File.ReadAllText(settingsFile + ".quick")) ?? new QuickPrintStore();
            }
            catch { }
            return new QuickPrintStore();
        }

        bool SaveQuickPrint(QuickPrintStore store)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
                WorkflowStorage.Save(settingsFile + ".quick", store);
                if (quickPrint != null && !quickPrint.IsDisposed) quickPrint.Visible = store.A != null || store.B != null;
                return true;
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not save the print settings.\r\n" + ex.Message, "Register Quick Print"); return false; }
        }

        string PrintSettingsSummary(PrintOptions settings, string layoutId)
        {
            if (settings == null) return "Unregistered";
            PhotoLayout layout = FrameSpool.LayoutEngine.Catalog.FirstOrDefault(l => l.Id == layoutId);
            return "Printer: " + settings.Printer + "\r\nPaper Source: " + settings.Source +
                "\r\nPaper Size: " + settings.Paper + "\r\nPaper Type: " + settings.PaperType +
                "\r\nPrint Mode: " + settings.Mode + "\r\nBorderless: " + (settings.Borderless ? "Yes" : "No") +
                "\r\nFrame Title: " + (layout == null ? "Unavailable" : layout.Name) +
                "\r\nDate/File Name: " + (settings.PrintInfo ? settings.InfoFileName ? "File Name" : "Date" : "None");
        }

        void ShowQuickPrint()
        {
            QuickPrintStore store = ReadQuickPrint();
            using (Form dialog = Ui.Dialog("Register Quick Print", 555, 400))
            {
                RadioButton a = new RadioButton { Text = "A", Bounds = new Rectangle(15, 20, 290, 18), Checked = browserQuickPrintSlot != "B" };
                RadioButton b = new RadioButton { Text = "B", Bounds = new Rectangle(15, 40, 290, 18), Checked = browserQuickPrintSlot == "B" };
                dialog.Controls.Add(a); dialog.Controls.Add(b);
                Ui.Label(dialog, "Current Settings", 15, 65, 255, 18);
                Ui.Label(dialog, "Settings to Register", 280, 65, 255, 18);
                TextBox registered = BrowserText(dialog, 15, 88, 255); registered.Multiline = true; registered.ReadOnly = true; registered.Height = 167; registered.ScrollBars = ScrollBars.Vertical;
                TextBox proposed = BrowserText(dialog, 280, 88, 255); proposed.Multiline = true; proposed.ReadOnly = true; proposed.Height = 167; proposed.ScrollBars = ScrollBars.Vertical;
                proposed.Text = PrintSettingsSummary(Options, CurrentLayout == null ? null : CurrentLayout.Id);
                CheckBox shortcut = Ui.Check(dialog, "Create a shortcut icon on the desktop.", 15, 275, 520, false, null);
                CheckBox confirm = Ui.Check(dialog, "Display a confirmation message before printing.", 15, 305, 520, true, null);
                Action update = delegate {
                    QuickPrintSet set = b.Checked ? store.B : store.A;
                    a.Text = "A " + (store.A == null ? "Unregistered" : "Registered");
                    b.Text = "B " + (store.B == null ? "Unregistered" : "Registered");
                    registered.Text = set == null ? "Unregistered" : PrintSettingsSummary(set.Settings, set.LayoutId);
                    confirm.Checked = set == null || set.Confirm;
                    shortcut.Checked = set != null && set.DesktopShortcut;
                };
                a.CheckedChanged += delegate { if (a.Checked) update(); }; b.CheckedChanged += delegate { if (b.Checked) update(); };
                Ui.Button(dialog, "Clear Settings", 211, 365, 100, 21, delegate {
                    string slot=b.Checked?"B":"A";
                    if (b.Checked) store.B = null; else store.A = null;
                    if (SaveQuickPrint(store)){try{QuickPrintShortcut.Remove(slot);}catch(Exception error){MessageBox.Show(dialog,error.Message,"Quick Print Shortcut");}update();}
                });
                Ui.Button(dialog, "Save Settings", 326, 365, 100, 21, delegate {
                    if (CurrentLayout == null) return;
                    PrintOptions snapshot = new PrintOptions(); CopyOptions(Options, snapshot);
                    QuickPrintSet set = new QuickPrintSet { Settings = snapshot, LayoutId = CurrentLayout.Id, Confirm = confirm.Checked, DesktopShortcut=shortcut.Checked };
                    if (b.Checked) store.B = set; else store.A = set;
                    if (SaveQuickPrint(store)) { browserQuickPrintSlot = b.Checked ? "B" : "A";try{if(shortcut.Checked)QuickPrintShortcut.Create(QuickPrintShortcut.DesktopPath(browserQuickPrintSlot),browserQuickPrintSlot);else QuickPrintShortcut.Remove(browserQuickPrintSlot);}catch(Exception error){set.DesktopShortcut=false;SaveQuickPrint(store);MessageBox.Show(dialog,"The settings were saved, but the desktop shortcut could not be updated.\r\n"+error.Message,"Quick Print Shortcut");} PersistBrowserPreferences(); update(); }
                });
                Button close = Ui.Button(dialog, "Close", 441, 365, 100, 21, delegate { dialog.Close(); });
                Ui.Button(dialog, "Help", 441, 15, 100, 21, delegate { ShowHelp("Register Quick Print"); });
                dialog.CancelButton = close; update(); dialog.ShowDialog(this);
            }
        }

        void RunQuickPrint()
        { RunQuickPrint(null); }
        void RunQuickPrint(string requestedSlot)
        {
            if (Selected.Count == 0) return;
            QuickPrintStore store = ReadQuickPrint();
            string slot = requestedSlot ?? browserQuickPrintSlot;
            if (requestedSlot==null&&store.A != null && store.B != null)
            {
                using (Form chooser = Ui.Dialog("Quick Print", 300, 180))
                {
                    Ui.Label(chooser, "Print Using Registered Settings", 20, 15, 260, 22);
                    RadioButton a = new RadioButton { Text = "A Registered", Bounds = new Rectangle(25, 50, 250, 20), Checked = slot != "B" };
                    RadioButton b = new RadioButton { Text = "B Registered", Bounds = new Rectangle(25, 80, 250, 20), Checked = slot == "B" };
                    chooser.Controls.Add(a); chooser.Controls.Add(b);
                    Button ok = Ui.Button(chooser, "OK", 95, 135, 85, 24, delegate { slot = b.Checked ? "B" : "A"; chooser.DialogResult = DialogResult.OK; chooser.Close(); });
                    Button cancel = Ui.Button(chooser, "Cancel", 190, 135, 85, 24, delegate { chooser.Close(); });
                    chooser.AcceptButton = ok; chooser.CancelButton = cancel;
                    if (chooser.ShowDialog(this) != DialogResult.OK) return;
                }
            }
            else if(requestedSlot==null)slot = store.A != null ? "A" : "B";
            QuickPrintSet set = slot == "B" ? store.B : store.A;
            if (set == null || set.Settings == null) { MessageBox.Show(this, "No Quick Print settings are registered.", "Quick Print"); return; }
            PhotoLayout layout = FrameSpool.LayoutEngine.Catalog.FirstOrDefault(l => l.Id == set.LayoutId && l.Paper == set.Settings.Paper);
            if (layout == null || !PrinterSettings.InstalledPrinters.Cast<string>().Contains(set.Settings.Printer, StringComparer.OrdinalIgnoreCase))
            { MessageBox.Show(this, "Unable to find the printer driver or the layout registered in Quick Print. Please register the Quick Print settings again.", "Quick Print"); return; }
            if (set.Confirm && MessageBox.Show(this, "Prints selected photos with the following settings. Do you want to continue?\r\n\r\n" + PrintSettingsSummary(set.Settings, set.LayoutId), "Confirm Quick Print", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            int minimum=Options.MinimumImageSize;CopyOptions(set.Settings, Options);Options.MinimumImageSize=minimum; CurrentLayout = layout;
            browserQuickPrintSlot = slot; PersistBrowserPreferences();
            manualMode=false;pageCopyCounts.Clear();BuildPrinting(); BuildMenus(); PrintWithDriver(true,false);
        }
    }

    // A scrollable bitmap view which owns its currently displayed image.
    internal sealed class BrowserPhotoView : Panel
    {
        Bitmap displayed;
        bool fit = true;
        float zoom = 1f;
        public event Action ViewChanged;
        public BrowserPhotoView() { DoubleBuffered = true; BackColor = Color.White; BorderStyle = BorderStyle.Fixed3D; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool FitToWindow
        {
            get { return fit; }
            set { fit = value; UpdateExtent(); if (ViewChanged != null) ViewChanged(); }
        }
        public void SetImage(Bitmap bitmap)
        {
            Bitmap previous = displayed; displayed = bitmap;
            if (previous != null) previous.Dispose();
            UpdateExtent();
        }
        public void ZoomBy(float factor)
        {
            if (fit) return;
            zoom = Math.Max(.1f, Math.Min(4f, zoom * factor)); UpdateExtent();
        }
        void UpdateExtent()
        {
            AutoScroll = !fit;
            AutoScrollMinSize = displayed == null || fit ? Size.Empty : new Size((int)(displayed.Width * zoom), (int)(displayed.Height * zoom));
            Invalidate();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateExtent(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (displayed == null) return;
            float scale = fit ? Math.Min((float)ClientSize.Width / displayed.Width, (float)ClientSize.Height / displayed.Height) : zoom;
            float width = displayed.Width * scale, height = displayed.Height * scale;
            float x = fit || width < ClientSize.Width ? (ClientSize.Width - width) / 2 : AutoScrollPosition.X;
            float y = fit || height < ClientSize.Height ? (ClientSize.Height - height) / 2 : AutoScrollPosition.Y;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(displayed, new RectangleF(x, y, width, height), new RectangleF(0, 0, displayed.Width, displayed.Height), GraphicsUnit.Pixel);
        }
        protected override void Dispose(bool disposing)
        { if (disposing && displayed != null) { displayed.Dispose(); displayed = null; } base.Dispose(disposing); }
    }
}
