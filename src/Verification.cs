using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace FrameSpool
{
    public sealed class VerificationCheck
    {
        public string Name;
        public bool Passed;
        public string Detail;
    }

    public static class Verification
    {
        public static int PrintPdfTest(string outputDir)
        {
            string directory=Path.GetFullPath(outputDir),fixture=Path.Combine(directory,"source-fixture.png");
            if(!File.Exists(fixture))return 2;
            string printer=System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().FirstOrDefault(p=>p=="Microsoft Print to PDF");
            if(printer==null)return 3;
            try
            {
                FrameSpool.LayoutEngine.LoadEmbeddedCatalog();var layout=FrameSpool.LayoutEngine.Catalog.First(l=>l.Id=="03011201.fd2");
                var photo=new PhotoItem(fixture){Comment="Synthetic print verification"};photo.Adjustments.Mode="None";var page=new PhotoPage();page.Photos.Add(photo);
                var options=new PrintOptions{Printer=printer,Paper="3.5x5",PrintInfo=true};
                var report=new Dictionary<string,object>();string output=Path.Combine(directory,"windows-driver-print.pdf");
                using(var document=new System.Drawing.Printing.PrintDocument())
                {
                    document.DocumentName="FrameSpool verification";document.PrinterSettings.PrinterName=printer;document.PrinterSettings.PrintToFile=true;document.PrinterSettings.PrintFileName=output;document.PrintController=new System.Drawing.Printing.StandardPrintController();
                    PrinterProfile.Apply(document,layout,options);
                    document.PrintPage+=delegate(object sender,System.Drawing.Printing.PrintPageEventArgs e){report["dpiX"]=e.Graphics.DpiX;report["dpiY"]=e.Graphics.DpiY;report["pageWidthHundredths"]=e.PageBounds.Width;report["pageHeightHundredths"]=e.PageBounds.Height;report["hardMarginX"]=e.PageSettings.HardMarginX;report["hardMarginY"]=e.PageSettings.HardMarginY;PrinterProfile.DrawPrintedPage(e,layout,page,options,true);e.HasMorePages=false;};
                    document.Print();
                }
                for(int n=0;n<100&&!File.Exists(output);n++)System.Threading.Thread.Sleep(100);
                report["pdf"]=output;report["exists"]=File.Exists(output);report["printer"]=printer;report["sourceSHA256"]=HashFile(fixture);report["physicalPrintingTested"]=false;
                File.WriteAllText(Path.Combine(directory,"windows-driver-print.json"),new JavaScriptSerializer().Serialize(report));return File.Exists(output)?0:4;
            }
            catch(Exception error){File.WriteAllText(Path.Combine(directory,"windows-driver-print-error.txt"),error.ToString());return 1;}
        }
        static void Check(List<VerificationCheck> checks, string name, bool passed, string detail = "")
        { checks.Add(new VerificationCheck { Name = name, Passed = passed, Detail = detail }); }

        static bool Near(float actual, float expected)
        { return Math.Abs(actual - expected) < .00002f; }

        static bool SameColor(Color a, Color b, int tolerance)
        { return Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance; }

        static string HashFile(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return String.Concat(hash.ComputeHash(stream).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        static string CatalogPath()
        {
            return "embedded:" + AppAssets.ResourceName + "/layouts.json";
        }

        public static int Run(string outputDir)
        {
            List<VerificationCheck> checks = new List<VerificationCheck>();
            string directory = Path.GetFullPath(outputDir);
            string catalog = null, sourceHash = null;
            try
            {
                Directory.CreateDirectory(directory);
                catalog = CatalogPath();
                FrameSpool.LayoutEngine.LoadEmbeddedCatalog();
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 16000000 };
                List<Dictionary<string, object>> records = serializer.Deserialize<List<Dictionary<string, object>>>(AppAssets.ReadText("layouts.json"));
                Check(checks, "Catalog contains 135 FD2 and 8 roll records", records.Count == 143, "Source records: " + records.Count);
                Check(checks, "Catalog accepts all 143 recovered layouts", FrameSpool.LayoutEngine.Catalog.Count == 143, "Loaded layouts: " + FrameSpool.LayoutEngine.Catalog.Count);
                Check(checks, "Every accepted layout has at least one frame", FrameSpool.LayoutEngine.Catalog.All(l => l.Frames.Count > 0));
                Check(checks, "All accepted page dimensions are positive", FrameSpool.LayoutEngine.Catalog.All(l => l.Width > 0 && l.Height > 0));
                string[] missing = records.Select(r => Convert.ToString(r["file"]))
                    .Except(FrameSpool.LayoutEngine.Catalog.Select(l => l.Id)).ToArray();
                File.WriteAllText(Path.Combine(directory, "catalog-diagnostics.txt"), "Source records: " + records.Count +
                    "\r\nAccepted layouts: " + FrameSpool.LayoutEngine.Catalog.Count +
                    "\r\nRecords without accepted frames: " + missing.Length +
                    "\r\n" + String.Join("\r\n", missing));

                PhotoLayout borderless = FrameSpool.LayoutEngine.Catalog.First(l => l.Id == "bo011201.fd2");
                PhotoLayout margin = FrameSpool.LayoutEngine.Catalog.First(l => l.Id == "03011201.fd2");
                PhotoLayout index = FrameSpool.LayoutEngine.Catalog.First(l => l.Id == "in201200.fd2");
                Check(checks,"Recovered captions distinguish date and comments",margin.Captions.Count==4&&margin.Captions.Count(c=>c.Role=="info")==2&&margin.Captions.Count(c=>c.Role=="comment")==2&&margin.Info&&margin.Comments);
                Check(checks,"Recovered caption keeps its exact coordinates and font size",Near(margin.Captions[0].Bounds.X,1217f/1800)&&Near(margin.Captions[0].Bounds.Y,1157f/1260)&&margin.Captions[0].Points==8);
                Check(checks,"Decoration containers are not photo slots",FrameSpool.LayoutEngine.Catalog.First(l=>l.Id=="sp011203.fd2").Frames.Count==1);
                Check(checks, "Borderless recovered page size", borderless.Width == 1800 && borderless.Height == 1260,
                    "bo011201.fd2 @360dpi: 1800 x 1260");
                Check(checks, "Borderless recovered single full-page frame", borderless.Frames.Count == 1 && borderless.Frames[0] == new RectangleF(0, 0, 1, 1));
                RectangleF mf = margin.Frames[0];
                Check(checks, "3-mm layout exact recovered origin", Near(mf.X, 45f / 1800f) && Near(mf.Y, 46f / 1260f),
                    "03011201.fd2 CSS margins42px + left3px/top4px");
                Check(checks, "3-mm layout exact recovered dimensions", Near(mf.Width, 1711f / 1800f) && Near(mf.Height, 1170f / 1260f));
                Check(checks, "Index20 frame count", index.Frames.Count == 20);
                RectangleF firstIndex = index.Frames[0], lastIndex = index.Frames[19];
                Check(checks, "Index20 exact first origin", Near(firstIndex.X, 134f / 1800f) && Near(firstIndex.Y, 120f / 1260f),
                    "in201200.fd2 CSS margin42px + left92px/top78px");
                Check(checks, "Index20 exact photo dimensions", Near(firstIndex.Width, 285f / 1800f) && Near(firstIndex.Height, 214f / 1260f));
                Check(checks, "Index20 exact last origin", Near(lastIndex.X, 1382f / 1800f) && Near(lastIndex.Y, 927f / 1260f));
                Check(checks, "Paper filtering preserves identifiers", FrameSpool.LayoutEngine.ForPaper("3.5x5").All(l => l.Paper == "3.5x5") &&
                    FrameSpool.LayoutEngine.ForPaper("3.5x5").Any(l => l.Id == borderless.Id));

                string fixture = Path.Combine(directory, "source-fixture.png");
                Color topLeft = Color.FromArgb(190, 50, 45), topRight = Color.FromArgb(50, 175, 70);
                Color bottomLeft = Color.FromArgb(45, 80, 190), bottomRight = Color.FromArgb(210, 180, 55);
                using (Bitmap image = new Bitmap(1200, 800))
                {
                    using (Graphics g = Graphics.FromImage(image))
                    using (Brush tl = new SolidBrush(topLeft))
                    using (Brush tr = new SolidBrush(topRight))
                    using (Brush bl = new SolidBrush(bottomLeft))
                    using (Brush br = new SolidBrush(bottomRight))
                    {
                        g.FillRectangle(tl, 0, 0, 600, 400); g.FillRectangle(tr, 600, 0, 600, 400);
                        g.FillRectangle(bl, 0, 400, 600, 400); g.FillRectangle(br, 600, 400, 600, 400);
                        g.FillRectangle(Brushes.White, 80, 50, 110, 70);
                        g.FillRectangle(Brushes.Black, 95, 65, 20, 35);
                        g.FillEllipse(Brushes.Red, 294, 194, 12, 12);
                    }
                    image.Save(fixture, ImageFormat.Png);
                }
                File.SetLastWriteTime(fixture, new DateTime(2016, 7, 31, 17, 30, 9));
                sourceHash = HashFile(fixture);
                PhotoItem photo = new PhotoItem(fixture);
                photo.Adjustments.Mode = "None";
                Check(checks, "Fixture loads without private user images", photo.Width == 1200 && photo.Height == 800);
                Check(checks, "Non-EXIF capture date falls back to file modification date", photo.CaptureDate == new DateTime(2016, 7, 31, 17, 30, 9));
                Check(checks, "Minimum image dimension boundary", PhotoEngine.ValidateDimensions(60, 600) == null && PhotoEngine.ValidateDimensions(59, 600) != null);
                Check(checks, "Aspect ratio boundary", PhotoEngine.ValidateDimensions(60, 900) == null && PhotoEngine.ValidateDimensions(60, 901) != null);
                photo.Copies = 100;
                Check(checks, "Photo copies clamp at 99", photo.Copies == 99);
                photo.Copies = -1;
                Check(checks, "Photo copies clamp at zero", photo.Copies == 0);
                photo.Copies = 99;
                var stickers=FrameSpool.LayoutEngine.Catalog.First(l=>l.Id=="sp041303.fd2");photo.Copies=2;
                var stickerPages=FrameSpool.LayoutEngine.Paginate(new[]{photo},stickers);
                Check(checks,"Same-photo stickers fill every opening from one selected copy",stickerPages.Count==2&&stickerPages.All(p=>p.Photos.Count==4&&p.Photos.All(item=>item.Original==photo)));
                stickerPages[0].Photos[0].Rotation=1;
                Check(checks,"Positioning stays local to each placed copy",stickerPages[0].Photos[1].Rotation==0&&stickerPages[1].Photos[0].Rotation==0&&photo.Rotation==0);
                photo.Adjustments.Vivid=true;Check(checks,"Placed copies share source color correction settings",stickerPages.All(p=>p.Photos.All(item=>item.Adjustments.Vivid)));photo.Adjustments.Vivid=false;
                photo.Copies=99;
                List<PhotoPage> pages = FrameSpool.LayoutEngine.Paginate(new PhotoItem[] { photo }, index);
                Check(checks, "99 photos paginate into five Index20 pages", pages.Count == 5);
                Check(checks, "Index20 pagination fills frames and preserves remainder", pages.Take(4).All(p => p.Photos.Count == 20) && pages[4].Photos.Count == 19);
                Check(checks, "Page copies default to one", pages.All(p => p.Copies == 1));
                pages[0].Copies = 2; pages[4].Copies = 3;
                Check(checks, "Individual page counts contribute correctly to total prints", pages.Sum(p => p.Copies) == 8);
                PhotoItem unselected = photo.Clone(); unselected.Copies = 0;
                Check(checks, "Zero-copy photos do not create pages", FrameSpool.LayoutEngine.Paginate(new PhotoItem[] { unselected }, borderless).Count == 0);
                List<PhotoItem> overflow = new List<PhotoItem>();
                for (int n = 0; n < 11; n++) overflow.Add(photo.Clone());
                Check(checks, "Printing stops at the original 999-page boundary", FrameSpool.LayoutEngine.Paginate(overflow, borderless).Count == 999);
                List<PhotoItem> selected999 = new List<PhotoItem>();
                for (int n = 0; n < 999; n++) { PhotoItem clone = photo.Clone(); clone.Copies = 1; selected999.Add(clone); }
                List<PhotoPage> many = FrameSpool.LayoutEngine.Paginate(selected999, index);
                Check(checks, "999 selected instances paginate without loss", many.Count == 50 && many.Sum(p => p.Photos.Count) == 999 && many.Last().Photos.Count == 19);
                Check(checks, "Position zoom clamps to50..200 percent", SetZoom(photo, 10f) == 2f && SetZoom(photo, .01f) == .5f);
                photo.ResetPosition(); photo.Rotation = 1;
                using (Bitmap rotated = PhotoEngine.RenderImage(photo, false))
                {
                    Check(checks, "90-degree rotation swaps pixel dimensions", rotated.Width == 800 && rotated.Height == 1200);
                    Check(checks, "Asymmetric quadrant rotation is clockwise", SameColor(rotated.GetPixel(700, 250), topLeft, 1));
                }
                photo.ResetPosition();
                photo.Comment = new string('x', 75);
                Check(checks, "Comment limit is60 characters", photo.Comment.Length == 60);
                Check(checks, "Manual red-eye accepts first20 positions", AddTwentyEyes(photo));
                Check(checks, "Manual red-eye rejects21st position", !PhotoEngine.AddManualEye(photo, new PointF(.5f, .5f)));
                PhotoItem independent = photo.Clone(); independent.Adjustments.ManualEyePoints.Clear();
                Check(checks, "Cloned manual correction lists are independent", photo.Adjustments.ManualEyePoints.Count == 20);
                photo.ResetCorrection(); photo.Adjustments.Mode = "None";
                PhotoEngine.AddManualEye(photo, new PointF(300f / 1199f, 200f / 799f));
                using (Bitmap original = PhotoEngine.RenderImage(photo, false))
                using (Bitmap corrected = PhotoEngine.RenderImage(photo, true))
                {
                    Check(checks, "Manual red-eye leaves original preview intact", original.GetPixel(300, 200).R > 230);
                    Check(checks, "Manual red-eye changes corrected pixels", corrected.GetPixel(300, 200).R < 50);
                }
                photo.ResetCorrection(); photo.Adjustments.Mode = "None";
                PhotoPage one = new PhotoPage(); one.Photos.Add(photo);
                PrintOptions options = new PrintOptions { Paper = "3.5x5", PrintInfo = false };
                string previewBefore = Path.Combine(directory, "original-preview-before.png");
                using (Bitmap page = FrameSpool.LayoutEngine.RenderPage(borderless, one, options, 300, false))
                {
                    Check(checks, "300dpi borderless export has exact1500x1050 pixels", page.Width == 1500 && page.Height == 1050);
                    Check(checks, "Export bitmap carries300dpi", Math.Abs(page.HorizontalResolution - 300) < .01f && Math.Abs(page.VerticalResolution - 300) < .01f);
                    Check(checks, "Borderless output contains original image quadrants", SameColor(page.GetPixel(250, 250), topLeft, 2) && SameColor(page.GetPixel(1200, 800), bottomRight, 2));
                    page.Save(previewBefore, ImageFormat.Png);
                }
                photo.Adjustments.Monochrome = true; photo.Adjustments.Brightness = 40;
                string previewAfter = Path.Combine(directory, "original-preview-after.png");
                string correctedPrint = Path.Combine(directory, "corrected-print.png");
                using (Bitmap page = FrameSpool.LayoutEngine.RenderPage(borderless, one, options, 300, false)) page.Save(previewAfter, ImageFormat.Png);
                using (Bitmap page = FrameSpool.LayoutEngine.RenderPage(borderless, one, options, 300, true))
                {
                    Color pixel = page.GetPixel(250, 250);
                    Check(checks, "Corrected print applies monochrome and brightness", pixel.R == pixel.G && pixel.G == pixel.B && !SameColor(pixel, topLeft, 5));
                    page.Save(correctedPrint, ImageFormat.Png);
                }
                Check(checks, "Main preview stays identical after correction settings", HashFile(previewBefore) == HashFile(previewAfter));
                Check(checks, "Corrected print differs from main preview", HashFile(previewBefore) != HashFile(correctedPrint));
                using (Bitmap reopened = new Bitmap(correctedPrint))
                    Check(checks, "PNG export preserves300dpi after reopening", Math.Abs(reopened.HorizontalResolution - 300) < .1f);
                photo.ResetCorrection(); photo.Adjustments.Mode = "None";
                using (Bitmap page = FrameSpool.LayoutEngine.RenderPage(margin, one, options, 300, false))
                {
                    Check(checks, "Recovered3mm layout keeps exterior page area white", page.GetPixel(5, 5).ToArgb() == Color.White.ToArgb());
                    Check(checks, "Recovered3mm image content appears inside frame", SameColor(page.GetPixel(250, 250), topLeft, 2));
                    page.Save(Path.Combine(directory, "3mm-margin-print.png"), ImageFormat.Png);
                }
                photo.Copies = 20;
                PhotoPage twenty = FrameSpool.LayoutEngine.Paginate(new PhotoItem[] { photo }, index)[0];
                using (Bitmap page = FrameSpool.LayoutEngine.RenderPage(index, twenty, options, 300, false))
                {
                    Check(checks, "Index20 exterior margin stays white", page.GetPixel(5, 5).ToArgb() == Color.White.ToArgb());
                    Check(checks, "Index20 recovered first frame contains pixels", page.GetPixel(140, 130).ToArgb() != Color.White.ToArgb());
                    page.Save(Path.Combine(directory, "index20-print.png"), ImageFormat.Png);
                }
                using (Bitmap canvas = new Bitmap(220, 160))
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.White); photo.OffsetX = 40; photo.Zoom = 2;
                    PhotoEngine.DrawPhoto(g, photo, new RectangleF(40, 30, 120, 90), false);
                    Check(checks, "Crop zoom offset is clipped to the frame", canvas.GetPixel(39, 70).ToArgb() == Color.White.ToArgb() && canvas.GetPixel(161, 70).ToArgb() == Color.White.ToArgb());
                }
                photo.ResetPosition(); photo.Comment = "Synthetic comment fixture";
                PhotoLayout annotated = new PhotoLayout { Id = "verification-annotation", Name = "Verification annotation", Paper = "3.5x5", Width = 1800, Height = 1260, Info = true, Comments = true };
                annotated.Frames.Add(new RectangleF(.05f, .05f, .9f, .8f));
                string noInfoPath = Path.Combine(directory, "annotations-disabled.png"), infoPath = Path.Combine(directory, "date-and-comment-print.png");
                // Annotation test checks the renderer, not equivalence of text-layout algorithms.
                string comment = photo.Comment; photo.Comment = "";
                using (Bitmap page = FrameSpool.LayoutEngine.RenderPage(annotated, one, options, 100, false)) page.Save(noInfoPath, ImageFormat.Png);
                photo.Comment = comment; options.PrintInfo = true; options.ShowTime = true; options.ShowSeconds = true;
                using (Bitmap page = FrameSpool.LayoutEngine.RenderPage(annotated, one, options, 100, false)) page.Save(infoPath, ImageFormat.Png);
                Check(checks, "Enabled date and comments change rendered output", HashFile(noInfoPath) != HashFile(infoPath));
                using (Bitmap thumbnail = PhotoEngine.CreateThumbnail(photo, 180, 120))
                    Check(checks, "Thumbnail generation returns requested dimensions", thumbnail.Width == 180 && thumbnail.Height == 120);
                Check(checks, "All operations preserve source file SHA256", HashFile(fixture) == sourceHash, sourceHash);
                byte[] redYcc=FixedPointColorMath.ToYcc(Color.Red),whiteYcc=FixedPointColorMath.ToYcc(Color.White);
                Check(checks,"Recovered fixed-point RGB-to-YCC reference vectors",redYcc.SequenceEqual(new byte[]{76,84,255})&&whiteYcc.SequenceEqual(new byte[]{254,127,127}));
                Check(checks,"Recovered fixed-point YCC inverse reference vectors",FixedPointColorMath.FromYcc(0,128,128).ToArgb()==Color.Black.ToArgb()&&FixedPointColorMath.FromYcc(76,84,255).ToArgb()==Color.FromArgb(254,0,0).ToArgb());
                photo.OffsetX=60;photo.OffsetY=0;photo.Comment="";options.PrintInfo=false;
                using(var small=FrameSpool.LayoutEngine.RenderPage(borderless,one,options,180,false))
                using(var large=FrameSpool.LayoutEngine.RenderPage(borderless,one,options,360,false))
                {Check(checks,"Positioning offsets scale consistently with output resolution",SameColor(small.GetPixel(475,80),large.GetPixel(950,160),2)&&SameColor(small.GetPixel(485,80),large.GetPixel(970,160),2));}
                LegacyFormatVerification.Run(directory,checks);
                PhotoDecodingVerification.Run(directory,(name,passed)=>Check(checks,name,passed));
                MainForm.VerifyVisuals(directory,fixture,checks);
                MainForm.VerifyNavigation(directory,fixture,checks);
                MainForm.VerifyWorkflows(directory,fixture,checks);
                DriverVerification.Run(directory,checks);
                UpdateVerification.Run(directory,checks);
                Check(checks,"Driver mode rejects truncated or corrupt buffers",!DriverSettings.IsValidMode(new byte[20])&&!DriverSettings.IsValidMode(new byte[72]));
                string pdfPrinter=System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().FirstOrDefault(p=>p=="Microsoft Print to PDF");
                if(pdfPrinter!=null)
                using(var document=new System.Drawing.Printing.PrintDocument())
                using(var restored=new System.Drawing.Printing.PrintDocument())
                {
                    var preset=new PrintOptions{Printer=pdfPrinter,Paper="3.5x5"};document.PrinterSettings.PrinterName=pdfPrinter;
                    PrinterProfile.Apply(document,borderless,preset);DriverSettings.Capture(document,preset);
                    restored.PrinterSettings.PrinterName=pdfPrinter;
                    Check(checks,"Windows driver settings round-trip",DriverSettings.Apply(restored,preset)&&restored.DefaultPageSettings.Landscape&&restored.DefaultPageSettings.PaperSize.Width==document.DefaultPageSettings.PaperSize.Width&&restored.DefaultPageSettings.PaperSize.Height==document.DefaultPageSettings.PaperSize.Height);
                    preset.DriverModePrinter="A different printer";Check(checks,"Device-specific settings never transfer to another printer",!DriverSettings.Apply(restored,preset));
                }
            }
            catch (Exception ex) { Check(checks, "Verification completed without an exception", false, ex.ToString()); }
            int failed = checks.Count(c => !c.Passed);
            try
            {
                Directory.CreateDirectory(directory);
                Dictionary<string, object> report = new Dictionary<string, object> {
                    { "status", failed == 0 ? "passed" : "failed" }, { "assertions", checks.Count }, { "failed", failed },
                    { "catalog", catalog }, { "sourceSHA256", sourceHash }, { "checks", checks },
                    { "limitations", new string[] { "No physical printing or original runtime comparison is performed.",
                        PhotoEngine.CorrectionFidelityNote, "The 999-file UI selection limit is not exercised by this UI-free verification." } } };
                File.WriteAllText(Path.Combine(directory, "verification.json"), new JavaScriptSerializer { MaxJsonLength = 16000000 }.Serialize(report));
                File.WriteAllText(Path.Combine(directory, "verification.txt"),
                    "FrameSpool verification: " + (checks.Count - failed) + "/" + checks.Count + " passed.\r\n\r\n" +
                    String.Join("\r\n", checks.Select(c => (c.Passed ? "PASS " : "FAIL ") + c.Name + (c.Detail.Length == 0 ? "" : " — " + c.Detail))) +
                    "\r\n\r\nCorrections are independent approximations. Physical printing and runtime equivalence are not tested.\r\n");
            }
            catch { return 1; }
            return failed == 0 ? 0 : 1;
        }

        static float SetZoom(PhotoItem photo, float zoom) { photo.Zoom = zoom; return photo.Zoom; }
        static bool AddTwentyEyes(PhotoItem photo)
        {
            for (int n = 0; n < 20; n++) if (!PhotoEngine.AddManualEye(photo, new PointF(.25f, .25f))) return false;
            return true;
        }
    }
}
