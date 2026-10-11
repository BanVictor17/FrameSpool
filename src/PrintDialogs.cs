using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class MainForm
    {
        private Form positioningPalette;

        private sealed class PaperChoice
        {
            public string Code, Label;
            public override string ToString() { return Label; }
        }

        private static string PaperLabel(string code)
        {
            switch (code)
            {
                case "3.5x5": return "9 x 13 cm (3.5 x 5 in)";
                case "4x6NP": return "10 x 15 cm (4 x 6 in)";
                case "4x7.11": return "10 x 18 cm (4 x 7.11 in)";
                case "5x7": return "13 x 18 cm (5 x 7 in)";
                case "8x10": return "20 x 25 cm (8 x 10 in)";
                case "10x12": return "25 x 30 cm (10 x 12 in)";
                case "HAGAKI": return "Postcard (100 x 148 mm)";
                case "A6": return "A6 (105 x 148 mm)";
                case "A4": return "A4 (210 x 297 mm)";
                case "A3": return "A3 (297 x 420 mm)";
                case "A3N": return "A3+ (13 x 19 in)";
                case "Letter": return "Letter (8.5 x 11 in)";
                case "R100": return "Roll Paper (100 mm)";
                case "R5IN": return "Roll Paper (5 in)";
                case "RA4": return "Roll Paper (210 mm / A4)";
                case "RA3N": return "Roll Paper (329 mm / A3+)";
                default: return code;
            }
        }

        private static ComboBox PrintCombo(Control parent, Rectangle bounds)
        {
            ComboBox combo = new ComboBox { Bounds = bounds, Font = Ui.Font(12),
                DropDownStyle = ComboBoxStyle.DropDownList, IntegralHeight = false, DropDownHeight = 230 };
            parent.Controls.Add(combo);
            return combo;
        }

        private static void SetPrintChoices(ComboBox combo, IEnumerable<string> values, string selected)
        {
            combo.BeginUpdate();
            combo.Items.Clear();
            foreach (string value in values.Where(v => !String.IsNullOrEmpty(v)).Distinct(StringComparer.OrdinalIgnoreCase))
                combo.Items.Add(value);
            if (!String.IsNullOrEmpty(selected) && !combo.Items.Contains(selected)) combo.Items.Add(selected);
            if (!String.IsNullOrEmpty(selected) && combo.Items.Contains(selected)) combo.SelectedItem = selected;
            else if (combo.Items.Count != 0) combo.SelectedIndex = 0;
            combo.EndUpdate();
        }

        void ShowPrinterSettings()
        {
            using (Form dialog = Ui.Dialog("Printer Settings", 300, 550))
            using (ToolTip tooltips = new ToolTip())
            {
                var staged=new PrintOptions();CopyOptions(Options,staged);
                Ui.Label(dialog, "Printer", 20, 32, 261, 16);
                ComboBox printers = PrintCombo(dialog, new Rectangle(28, 48, 250, 24));
                Ui.Label(dialog, "Paper Source", 20, 72, 261, 16);
                ComboBox sources = PrintCombo(dialog, new Rectangle(28, 88, 250, 24));
                CheckBox borderless = Ui.Check(dialog, "Borderless", 180, 116, 100, Options.Borderless, null);
                Ui.Label(dialog, "Paper Size", 20, 133, 261, 16);
                ComboBox papers = PrintCombo(dialog, new Rectangle(28, 149, 250, 24));
                Ui.Label(dialog, "Paper Type", 20, 173, 261, 16);
                ComboBox media = PrintCombo(dialog, new Rectangle(28, 189, 250, 24));
                Ui.Label(dialog, "Print Mode", 20, 213, 261, 16);
                ComboBox modes = PrintCombo(dialog, new Rectangle(28, 229, 250, 24));
                Ui.Label(dialog, "Borderless", 20, 253, 261, 16);
                Ui.Label(dialog, "Min", 20, 282, 72, 16).TextAlign = ContentAlignment.MiddleRight;
                TrackBar expansion = new TrackBar { Bounds = new Rectangle(100, 269, 86, 40),
                    Minimum = 0, Maximum = 2, TickStyle = TickStyle.None, Value = Math.Max(0, Math.Min(2, Options.Expansion)) };
                dialog.Controls.Add(expansion);
                Ui.Label(dialog, "Max", 194, 282, 100, 16);
                Ui.Label(dialog, "Caution", 20, 305, 261, 16);
                TextBox caution = new TextBox { Bounds = new Rectangle(28, 325, 250, 140),
                    Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White,
                    Font = Ui.Font(12), Text = "Print quality in the top and bottom areas may decline or the area may be smeared depending on the media. For details about this setting, see online help or your manual." };
                dialog.Controls.Add(caution);
                caution.Text += "\r\n\r\nFor printer-specific media, quality and borderless settings (including L8050), use Properties.";
                Ui.Button(dialog,"Properties...",190,479,95,23,delegate
                {
                    staged.Printer=printers.Text;staged.Source=sources.Text;staged.PaperType=media.Text;staged.Mode=modes.Text;
                    var choice=papers.SelectedItem as PaperChoice;if(choice!=null)staged.Paper=choice.Code;
                    var layout=FrameSpool.LayoutEngine.ForPaper(staged.Paper).FirstOrDefault();if(layout==null)return;
                    try{if(DriverSettings.Configure(dialog,layout,staged)){SetPrintChoices(sources,sources.Items.Cast<object>().Select(o=>o.ToString()).ToArray(),staged.Source);SetPrintChoices(media,new[]{"Printer Properties"},staged.PaperType);SetPrintChoices(modes,new[]{"Printer Properties"},staged.Mode);}}
                    catch(Exception error){MessageBox.Show(dialog,error.Message,"Printer Properties");}
                });
                Ui.Button(dialog, "Help", 190, 15, 95, 21, delegate { ShowHelp("Printer Settings"); });
                Button ok = Ui.Button(dialog, "OK", 109, 514, 85, 23, delegate { dialog.DialogResult = DialogResult.OK; });
                Button cancel = Ui.Button(dialog, "Cancel", 200, 514, 85, 23, delegate { dialog.DialogResult = DialogResult.Cancel; });
                dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                List<string> installed = new List<string>();
                try { foreach (string name in PrinterSettings.InstalledPrinters) installed.Add(name); } catch { }
                SetPrintChoices(printers, installed, Options.Printer);
                if (printers.Items.Count == 0) printers.Enabled = false;

                string[] order = { "4x6NP", "3.5x5", "5x7", "8x10", "10x12", "4x7.11", "HAGAKI", "A6", "Letter", "A4", "A3", "A3N", "R100", "R5IN", "RA4", "RA3N" };
                foreach (string code in order.Concat(FrameSpool.LayoutEngine.Catalog.Select(l => l.Paper)).Distinct())
                    if (FrameSpool.LayoutEngine.ForPaper(code).Count > 0) papers.Items.Add(new PaperChoice { Code = code, Label = PaperLabel(code) });
                if (!papers.Items.Cast<PaperChoice>().Any(p => p.Code == Options.Paper))
                    papers.Items.Add(new PaperChoice { Code = Options.Paper, Label = PaperLabel(Options.Paper) });
                papers.SelectedItem = papers.Items.Cast<PaperChoice>().First(p => p.Code == Options.Paper);
                SetPrintChoices(media, new string[] { "Plain Paper", "Ultra Glossy", "Premium Glossy", "Premium Semigloss", "Glossy", "Photo", "Matte", "Photo Quality Ink Jet" }, Options.PaperType);
                SetPrintChoices(modes, new string[] { "Draft", "Text", "Text & Image", "Photo", "Best Photo" }, Options.Mode);

                Action loadCapabilities = delegate
                {
                    string preferredSource = sources.SelectedIndex < 0 ? Options.Source : sources.Text;
                    List<string> availableSources = new List<string>();
                    List<string> reportedPapers = new List<string>();
                    List<string> reportedModes = new List<string>();
                    try
                    {
                        PrinterSettings settings = new PrinterSettings();
                        if (printers.SelectedIndex >= 0) settings.PrinterName = printers.Text;
                        if (settings.IsValid)
                        {
                            foreach (PaperSource source in settings.PaperSources) availableSources.Add(source.SourceName);
                            foreach (PaperSize size in settings.PaperSizes) reportedPapers.Add(size.PaperName);
                            foreach (PrinterResolution resolution in settings.PrinterResolutions)
                                reportedModes.Add(resolution.X > 0 && resolution.Y > 0
                                    ? resolution.X + " x " + resolution.Y + " dpi" : resolution.Kind.ToString());
                        }
                    }
                    catch { }
                    if (availableSources.Count == 0) availableSources.Add("Sheet");
                    SetPrintChoices(sources, availableSources, preferredSource);
                    string capabilities = reportedPapers.Count == 0 ? "" : "Paper sizes reported by the selected printer:\r\n" + String.Join("\r\n", reportedPapers.Distinct().ToArray());
                    tooltips.SetToolTip(papers, capabilities);
                    SetPrintChoices(modes, new string[] { "Draft", "Text", "Text & Image", "Photo", "Best Photo" }.Concat(reportedModes), modes.Text);
                };
                printers.SelectedIndexChanged += delegate { loadCapabilities(); };
                loadCapabilities();
                Action updateBorderless = delegate { expansion.Enabled = borderless.Checked; };
                borderless.CheckedChanged += delegate { updateBorderless(); }; updateBorderless();

                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                PaperChoice paper = papers.SelectedItem as PaperChoice;
                bool paperChanged = paper != null && paper.Code != Options.Paper;
                Options.Printer = printers.SelectedIndex >= 0 ? printers.Text : Options.Printer;
                Options.Source = sources.Text; Options.PaperType = media.Text; Options.Mode = modes.Text;
                Options.DriverMode=staged.DriverMode;Options.DriverModePrinter=staged.DriverModePrinter;Options.DriverModePaper=staged.DriverModePaper;Options.DriverModeLandscape=staged.DriverModeLandscape;
                Options.Borderless = borderless.Checked; Options.Expansion = expansion.Value;
                if (paper != null) Options.Paper = paper.Code;
                if (paperChanged)
                {
                    CurrentLayout = FrameSpool.LayoutEngine.ForPaper(Options.Paper).FirstOrDefault();
                    pageIndex = 0;
                    RadioButton photoLayout = photoLayoutRadio;
                    if (photoLayout != null) photoLayout.Checked = true;
                    FillLayouts(false);
                }
                UpdatePrinterSummary(); UpdatePage();
            }
        }

        private List<PhotoItem> CurrentPositionPhotos()
        {
            if (Pages.Count == 0 || pageIndex < 0 || pageIndex >= Pages.Count) return new List<PhotoItem>();
            return Pages[pageIndex].Photos.Where(p => p != null).Distinct().ToList();
        }

        void ShowPositioning()
        {
            if (positioningPalette != null && !positioningPalette.IsDisposed)
            { positioningPalette.Show(); positioningPalette.BringToFront(); return; }
            if (FocusPhoto == null) Focus(CurrentPositionPhotos().FirstOrDefault());
            if (FocusPhoto == null) return;
            Form dialog = Ui.Dialog("Positioning", 224, 460);
            positioningPalette = dialog;
            dialog.ShowInTaskbar = false;
            Ui.Label(dialog, "Magnification", 25, 14, 185, 16);
            Ui.Label(dialog, "−", 114, 30, 40, 16); Ui.Label(dialog, "+", 176, 30, 38, 16);
            TextBox value = new TextBox { Bounds = new Rectangle(33, 54, 34, 22), Font = Ui.Font(12), TextAlign = HorizontalAlignment.Center };
            dialog.Controls.Add(value); Ui.Label(dialog, "%", 72, 57, 26, 20);
            TrackBar zoom = new TrackBar { Bounds = new Rectangle(104, 48, 90, 42), Minimum = 50, Maximum = 200,
                TickFrequency = 50, SmallChange = 1, LargeChange = 10 };
            dialog.Controls.Add(zoom);
            bool syncing = false;
            CheckBox rotateAll = Ui.Check(dialog, "Rotate all Photos", 25, 314, 185, false, null);
            rotateAll.Height = 30;
            List<Control> photoControls = new List<Control> { value, zoom };
            Action sync = delegate
            {
                if (dialog.IsDisposed) return;
                syncing = true;
                bool hasPhoto = FocusPhoto != null;
                foreach (Control control in photoControls) control.Enabled = hasPhoto;
                if (hasPhoto)
                {
                    int number = Math.Max(50, Math.Min(200, (int)Math.Round(FocusPhoto.Zoom * 100)));
                    zoom.Value = number;
                    if (!value.Focused) value.Text = number.ToString();
                }
                rotateAll.Enabled = CurrentPositionPhotos().Count != 0;
                syncing = false;
            };
            Action refresh = delegate { UpdatePage(); sync(); };
            Action commitZoom = delegate
            {
                if (syncing || FocusPhoto == null) return;
                int number;
                if (!Int32.TryParse(value.Text, out number)) number = (int)Math.Round(FocusPhoto.Zoom * 100);
                number = Math.Max(50, Math.Min(200, number));
                FocusPhoto.Zoom = number / 100f;
                syncing = true; value.Text = number.ToString(); zoom.Value = number; syncing = false;
                UpdatePage();
            };
            value.TextChanged += delegate
            {
                int number;
                if (!syncing && FocusPhoto != null && Int32.TryParse(value.Text, out number) && number >= 50 && number <= 200)
                { FocusPhoto.Zoom = number / 100f; syncing = true; zoom.Value = number; syncing = false; UpdatePage(); }
            };
            value.Leave += delegate { commitZoom(); };
            value.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { commitZoom(); e.SuppressKeyPress = true; } };
            zoom.ValueChanged += delegate
            {
                if (!syncing && FocusPhoto != null) { FocusPhoto.Zoom = zoom.Value / 100f; syncing = true; value.Text = zoom.Value.ToString(); syncing = false; UpdatePage(); }
            };
            Button trim = Ui.Button(dialog, "Trim", 12, 94, 90, 27, delegate
            { if (FocusPhoto != null) { FocusPhoto.Fit = false; FocusPhoto.Zoom = 1f; FocusPhoto.OffsetX = FocusPhoto.OffsetY = 0; refresh(); } });
            Button fit = Ui.Button(dialog, "Fit", 122, 94, 90, 27, delegate
            { if (FocusPhoto != null) { FocusPhoto.Fit = true; FocusPhoto.Zoom = 1f; FocusPhoto.OffsetX = FocusPhoto.OffsetY = 0; refresh(); } });
            photoControls.Add(trim); photoControls.Add(fit);
            Ui.Label(dialog, "Move Photo", 25, 129, 185, 16);
            Action<int, int> move = delegate(int x, int y)
            { if (FocusPhoto != null) { FocusPhoto.OffsetX += x; FocusPhoto.OffsetY += y; refresh(); } };
            photoControls.Add(Ui.Skin(dialog, "", "Image/btn_2-3-08_u.bmp", 90, 153, 37, 25, delegate { move(0, -1); }));
            photoControls.Add(Ui.Skin(dialog, "", "Image/btn_2-3-09_u.bmp", 45, 186, 37, 25, delegate { move(-1, 0); }));
            photoControls.Add(Ui.Skin(dialog, "", "Image/btn_2-3-10_u.bmp", 90, 186, 37, 25, delegate
            { if (FocusPhoto != null) { FocusPhoto.OffsetX = FocusPhoto.OffsetY = 0; refresh(); } }));
            photoControls.Add(Ui.Skin(dialog, "", "Image/btn_2-3-11_u.bmp", 135, 186, 37, 25, delegate { move(1, 0); }));
            photoControls.Add(Ui.Skin(dialog, "", "Image/btn_2-3-12_u.bmp", 90, 219, 37, 25, delegate { move(0, 1); }));
            Ui.Label(dialog, "Rotate", 25, 254, 185, 16);
            Action<int> rotate = delegate(int direction)
            {
                IEnumerable<PhotoItem> photos = rotateAll.Checked ? CurrentPositionPhotos() : new List<PhotoItem> { FocusPhoto };
                foreach (PhotoItem photo in photos.Where(p => p != null)) photo.Rotation += direction;
                refresh();
            };
            photoControls.Add(Ui.Skin(dialog, "", "Image/btn_2-3-14_u.bmp", 68, 278, 37, 25, delegate { rotate(-1); }));
            photoControls.Add(Ui.Skin(dialog, "", "Image/btn_2-3-15_u.bmp", 113, 278, 37, 25, delegate { rotate(1); }));
            Label separator = Ui.Label(dialog, "", 10, 352, 204, 2); separator.BorderStyle = BorderStyle.Fixed3D;
            Button undo = Ui.Button(dialog, "Undo (Selected Photo)", 12, 362, 200, 21, delegate
            { if (FocusPhoto != null) { FocusPhoto.ResetPosition(); refresh(); } }); photoControls.Add(undo);
            Button undoAll = Ui.Button(dialog, "Undo (All Photos)", 12, 391, 200, 21, delegate
            { foreach (PhotoItem photo in CurrentPositionPhotos()) photo.ResetPosition(); refresh(); });
            Ui.Button(dialog, "Close", 127, 423, 85, 21, delegate { dialog.Close(); });
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 150 };
            PhotoItem observed = null;
            int observedPage = pageIndex;
            timer.Tick += delegate
            {
                if (!printingScreen) { dialog.Close(); return; }
                if (observedPage != pageIndex) { observedPage = pageIndex; Focus(CurrentPositionPhotos().FirstOrDefault()); }
                if (observed != FocusPhoto) { observed = FocusPhoto; syncing = true; value.Text = FocusPhoto == null ? "" : ((int)Math.Round(FocusPhoto.Zoom * 100)).ToString(); syncing = false; }
                undoAll.Enabled = CurrentPositionPhotos().Count != 0;
                sync();
            };
            dialog.FormClosed += delegate { timer.Stop(); timer.Dispose(); positioningPalette = null; };
            syncing = true; value.Text = ((int)Math.Round(FocusPhoto.Zoom * 100)).ToString(); syncing = false;
            sync(); timer.Start();
            dialog.StartPosition = FormStartPosition.Manual;
            Rectangle working = Screen.FromControl(this).WorkingArea;
            dialog.Location = new Point(Math.Min(Right + 4, working.Right - dialog.Width), Math.Max(working.Top, Top + 50));
            dialog.Show(this);
        }

        void ShowPageCounts()
        {
            if (Pages.Count == 0 || CurrentLayout == null) return;
            using (Form dialog = Ui.Dialog("Number of Pages", 792, 540))
            {
                Ui.Label(dialog, "You can specify the number of copies you want to print for each page.", 25, 19, 638, 20);
                Ui.Button(dialog, "Help", 684, 25, 87, 21, delegate { ShowHelp("Number of Pages"); });
                FlowLayoutPanel pages = new FlowLayoutPanel { Bounds = new Rectangle(15, 58, 762, 418),
                    AutoScroll = true, WrapContents = true, BackColor = Color.FromArgb(233, 236, 240),
                    BorderStyle = BorderStyle.Fixed3D, Padding = new Padding(8) };
                dialog.Controls.Add(pages);
                Label total = Ui.Label(dialog, "Total Prints:", 25, 493, 92, 33); total.TextAlign = ContentAlignment.MiddleLeft;
                Label totalValue = Ui.Label(dialog, Pages.Sum(p => p.Copies).ToString(), 117, 493, 38, 33); totalValue.TextAlign = ContentAlignment.MiddleRight;
                Ui.Label(dialog, "prints", 157, 493, 70, 33).TextAlign = ContentAlignment.MiddleLeft;
                Ui.Label(dialog, "Copies per Page:", 227, 493, 125, 33).TextAlign = ContentAlignment.MiddleRight;
                NumericUpDown all = Ui.Number(dialog, 354, 493, 44, 1, 99, 1, null);
                Ui.Label(dialog, "copies", 402, 494, 60, 23);
                List<NumericUpDown> counts = new List<NumericUpDown>();
                List<PagePreview> thumbnails = new List<PagePreview>();
                Action updateTotal = delegate { totalValue.Text = Pages.Sum(p => p.Copies).ToString(); UpdatePage(); };
                for (int i = 0; i < Pages.Count; i++)
                {
                    int index = i;
                    Panel card = new Panel { Size = new Size(165, 190), Margin = new Padding(5),
                        BackColor = Color.FromArgb(225, 226, 227), BorderStyle = BorderStyle.FixedSingle };
                    PagePreview thumbnail = new PagePreview { Bounds = new Rectangle(5, 5, 153, 155),
                        LayoutDefinition = CurrentLayout, Page = Pages[index], Options = Options, Corrected = false };
                    card.Controls.Add(thumbnail); thumbnails.Add(thumbnail);
                    Ui.Label(card, (index + 1).ToString(), 6, 166, 29, 18);
                    NumericUpDown count = Ui.Number(card, 43, 162, 47, 1, 99, Pages[index].Copies, delegate(int number)
                    { Pages[index].Copies = number; pageCopyCounts[index.ToString()] = number; updateTotal(); });
                    counts.Add(count); Ui.Label(card, "copies", 96, 166, 55, 18);
                    Action select = delegate { pageIndex = index; UpdatePage(); foreach (PagePreview image in thumbnails) image.BackColor = image == thumbnail ? Color.LightSteelBlue : Color.FromArgb(235, 236, 238); };
                    thumbnail.MouseDown += delegate { select(); }; card.MouseDown += delegate { select(); };
                    pages.Controls.Add(card);
                }
                Ui.Button(dialog, "Apply", 472, 494, 90, 21, delegate
                {
                    for (int i = 0; i < Pages.Count; i++)
                    { Pages[i].Copies = (int)all.Value; pageCopyCounts[i.ToString()] = (int)all.Value; counts[i].Value = all.Value; }
                    updateTotal();
                });
                Button close = Ui.Button(dialog, "Close", 672, 494, 90, 21, delegate { dialog.Close(); });
                dialog.CancelButton = close;
                dialog.ShowDialog(this);
            }
        }
    }
}
