using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace FrameSpool
{
    public static class LegacyFormatVerification
    {
        static void Check(List<VerificationCheck> checks,string name,bool passed,string detail="")
        {checks.Add(new VerificationCheck{Name=name,Passed=passed,Detail=detail});}
        static bool Near(float a,float b){return Math.Abs(a-b)<.01f;}
        public static void Run(string directory,List<VerificationCheck> checks)
        {
            Directory.CreateDirectory(directory);
            var catalog=LayoutEngine.Catalog;
            Check(checks,"Legacy roll catalog adds all eight USD definitions",catalog.Count(l=>l.Roll)==8&&catalog.Count==143);
            Check(checks,"Four original white EFF oval masks are bundled",catalog.Sum(l=>l.Decorations.Count)==4&&catalog.Where(l=>l.Decorations.Count>0).All(l=>l.Decorations.All(d=>AppAssets.Contains(d.Asset))));
            Check(checks,"Legacy ID layout crop-mark polylines recovered",catalog.Sum(l=>l.Lines.Count)==112&&catalog.First(l=>l.Id=="sp061500.fd2").Lines.Count==48);
            Check(checks,"Index layouts retain all 672 EXIF caption placements",catalog.Sum(l=>l.Captions.Count(c=>c.Role=="exif"))==672);
            string source=Path.Combine(directory,"legacy-solid.png");
            using(var bitmap=new Bitmap(640,480))using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.Red);bitmap.Save(source,ImageFormat.Png);}
            var photo=new PhotoItem(source);photo.Adjustments.Mode="None";var page=new PhotoPage();page.Photos.Add(photo);
            var vertical=catalog.First(l=>l.Id=="sp015100.usd");var horizontal=catalog.First(l=>l.Id=="sp015101.usd");
            var roll=LayoutEngine.ResolveForPage(vertical,page);var wide=LayoutEngine.ResolveForPage(horizontal,page);
            Check(checks,"100-mm roll width matches recovered 360-dpi dimensions",Near(roll.Width,1417)&&Near(roll.RollBleed,36));
            Check(checks,"Roll length follows photo aspect ratio and selected direction",Near(roll.Height,1417*4f/3)&&Near(wide.Height,1417*3f/4));
            Check(checks,"Roll resolution leaves original catalog template unchanged",!Object.ReferenceEquals(roll,vertical)&&Near(vertical.Height,1417*1.5f));
            Check(checks,"Roll photo bounds preserve 36-unit bleed on all sides",Near(roll.Frames[0].X*roll.Width,-36)&&Near(roll.Frames[0].Y*roll.Height,-36)&&Near(roll.Frames[0].Width*roll.Width,1489));
            var oval=catalog.First(l=>l.Id=="sp011203.fd2");
            using(var render=LayoutEngine.RenderPage(oval,page,new PrintOptions(),72,false))
            {render.Save(Path.Combine(directory,"legacy-oval.png"),ImageFormat.Png);Check(checks,"Original oval overlay leaves photo opening transparent",render.GetPixel(render.Width/2,render.Height/2).R>245&&render.GetPixel(render.Width/2,render.Height/2).G<10);Check(checks,"Original oval overlay paints opaque white corners",render.GetPixel(3,3).ToArgb()==Color.White.ToArgb());}
            photo.Metadata["EXP Time"]="1/125";photo.Metadata["F Number"]="5.6";photo.Metadata["ISO Sensitivity"]="200";photo.Metadata["Camera"]="Synthetic Camera";
            Check(checks,"EXIF index captions resolve source metadata tokens",LayoutEngine.CaptionText("exif","%E %U %I %C",photo,new PrintOptions())=="1/125 5.6 200 Synthetic Camera");
            photo.Metadata["Camera"]="";Check(checks,"Missing EXIF fields collapse cleanly",LayoutEngine.CaptionText("exif","%E %U %I %C",photo,new PrintOptions()).EndsWith("200"));
            Check(checks,"Invalid saved date pattern falls back without crashing",LayoutEngine.CaptionText("info","",photo,new PrintOptions{DateFormat="%"}).Length>0);
            using(var render=new Bitmap(200,100))using(var graphics=Graphics.FromImage(render))
            {
                graphics.Clear(Color.White);LayoutEngine.DrawCaption(graphics,new RectangleF(40,20,120,60),"Rotated text",new LayoutCaption{Rotation=180,Points=10,Color=Color.Black,Alignment="Center",VerticalAlignment="Center"},.25f);
                bool ink=false,outside=false;for(int y=0;y<render.Height;y++)for(int x=0;x<render.Width;x++)if(render.GetPixel(x,y).R<200){ink=true;if(x<40||x>=160||y<20||y>=80)outside=true;}
                Check(checks,"Rotated captions render and stay inside their recovered box",ink&&!outside);
            }
            var frame=new FrameDesign{Name="Portable test frame",Paper="3.5x5",Width=1800,Height=1260};
            frame.Elements.Add(new FrameElement{Kind="Opening",X=.1f,Y=.1f,Width=.8f,Height=.8f});
            frame.Elements.Add(new FrameElement{Kind="Decoration",X=0,Y=0,Width=.1f,Height=.1f,ImagePath=source});
            frame.Elements.Add(new FrameElement{Kind="Text",X=.1f,Y=.9f,Width=.8f,Height=.1f,CaptionRole="comment",CaptionPhotoIndex=0,Text="Comment",FontFamily="Arial",Points=8});
            string package=Path.Combine(directory,"portable-test.fspframe");LegacyFrames.Export(frame,package);var imported=LegacyFrames.Import(package);
            Check(checks,"Frame packages preserve editable geometry and dynamic captions",imported.Name==frame.Name&&imported.Elements.Count==3&&imported.Elements[2].CaptionRole=="comment"&&Near(imported.Elements[0].Width,.8f));
            var repeatFrame=new FrameDesign();repeatFrame.Elements.Add(new FrameElement{Kind="Opening",ImageGroup="same-photo",X=0,Y=0,Width=.5f,Height=1});repeatFrame.Elements.Add(new FrameElement{Kind="Opening",ImageGroup="same-photo",X=.5f,Y=0,Width=.5f,Height=1});photo.Copies=2;
            var repeatPages=LayoutEngine.Paginate(new[]{photo},repeatFrame.ToLayout());
            Check(checks,"Imported frame photo groups preserve repeated-photo sticker flow",repeatPages.Count==2&&repeatPages.All(p=>p.Photos.Count==2&&p.Photos.All(i=>i.Original==photo)));
            Check(checks,"Frame packages bundle independent decoration images",File.Exists(imported.Elements[1].ImagePath)&&!String.Equals(imported.Elements[1].ImagePath,source,StringComparison.OrdinalIgnoreCase));
            using(var original=Image.FromFile(source))using(var copy=Image.FromFile(imported.Elements[1].ImagePath))Check(checks,"Portable frame images preserve dimensions and colors",original.Width==copy.Width&&original.Height==copy.Height&&((Bitmap)copy).GetPixel(300,200).ToArgb()==Color.Red.ToArgb());
            frame.Name="Replaced portable frame";LegacyFrames.Export(frame,package);
            Check(checks,"Frame export replaces an existing package with complete updated content",LegacyFrames.Import(package).Name==frame.Name);
            byte[] previous=File.ReadAllBytes(package);bool locked=false;
            using(var held=File.Open(package,FileMode.Open,FileAccess.Read,FileShare.Read))
            {try{frame.Name="Must not replace locked frame";LegacyFrames.Export(frame,package);}catch(IOException){locked=true;}}
            Check(checks,"A persistently locked export preserves the previous frame and removes temporary files",locked&&previous.SequenceEqual(File.ReadAllBytes(package))&&Directory.GetFiles(directory,"portable-test.fspframe.*.tmp").Length==0);
            var brieflyHeld=File.Open(package,FileMode.Open,FileAccess.Read,FileShare.Read);
            var unlock=new System.Threading.Thread(delegate(){System.Threading.Thread.Sleep(120);brieflyHeld.Dispose();});unlock.Start();
            try{frame.Name="Frame after transient lock";LegacyFrames.Export(frame,package);}finally{unlock.Join();brieflyHeld.Dispose();}
            Check(checks,"Frame export retries a transient Windows file lock",LegacyFrames.Import(package).Name==frame.Name);
            bool truncated=false;try{LegacyFrames.ReadPifMembers(new byte[]{69,80,80,73,70});}catch(InvalidDataException){truncated=true;}
            Check(checks,"Truncated PIF input is rejected before reading members",truncated);
            string badPackage=Path.Combine(directory,"traversal-test.fspframe");
            using(var file=new FileStream(badPackage,FileMode.Create,FileAccess.ReadWrite))using(var archive=new ZipArchive(file,ZipArchiveMode.Create)){using(var writer=new StreamWriter(archive.CreateEntry("../outside.txt").Open()))writer.Write("invalid");}
            bool traversal=false;try{LegacyFrames.Import(badPackage);}catch(InvalidDataException){traversal=true;}
            Check(checks,"Portable frame imports reject paths outside the package",traversal);
        }
    }
}
