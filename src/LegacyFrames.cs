using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

namespace FrameSpool
{
    // Import the observed EPPIF 3.1 container family, plus a documented portable
    // FrameSpool package. Older PIF 2.x and colored EFF codecs remain unsupported.
    public static class LegacyFrames
    {
        const int MaximumPackageBytes=128*1024*1024,MaximumImageBytes=32*1024*1024;
        public static FrameDesign Import(string path)
        {
            string extension=Path.GetExtension(path).ToLowerInvariant();
            if(extension==".json")return FrameStore.Read(path);
            if(extension==".fspframe")return ImportPackage(path);
            byte[] data=ReadBounded(path,MaximumPackageBytes);
            if(data.Length>=6&&Encoding.ASCII.GetString(data,0,5)=="EPPIF")return ImportPif(data,Path.GetFileNameWithoutExtension(path));
            throw new InvalidDataException("This frame format is not supported. Import an EPPIF 3.1 FD2/PIF frame, FrameSpool JSON, or .fspframe package. Older PIF 2.x files must first be converted to PIF 3.1.");
        }
        public static void Export(FrameDesign design,string destination)
        {
            FrameStore.Validate(design);
            var copy=design.Clone();var images=new Dictionary<string,byte[]>();
            int number=0;
            foreach(var element in copy.Elements.Where(e=>e.Kind=="Decoration"))
            {
                if(String.IsNullOrEmpty(element.ImagePath)||!File.Exists(element.ImagePath))throw new FileNotFoundException("The frame's decoration image is missing.",element.ImagePath);
                byte[] image=ReadBounded(element.ImagePath,MaximumImageBytes);string name="images/"+(++number).ToString("000",CultureInfo.InvariantCulture)+".png";
                images.Add(name,ToPng(image));element.ImagePath=name;
            }
            string temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite))
                using(var archive=new ZipArchive(file,ZipArchiveMode.Create))
                {
                    WriteEntry(archive,"frame.json",Encoding.UTF8.GetBytes(new JavaScriptSerializer{MaxJsonLength=2000000}.Serialize(copy)));
                    WriteEntry(archive,"format.txt",Encoding.UTF8.GetBytes("FrameSpool Frame Package\r\nVersion: 1\r\nframe.json contains an editable FrameDesign; decoration paths refer to PNG entries.\r\nThis package is not a legacy PIF file.\r\n"));
                    foreach(var image in images)WriteEntry(archive,image.Key,image.Value);
                }
                AtomicFile.Commit(temporary,destination);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        static FrameDesign ImportPackage(string path)
        {
            if(new FileInfo(path).Length>MaximumPackageBytes)throw new InvalidDataException("The frame package is too large.");
            var files=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);long total=0;
            using(var file=File.OpenRead(path))using(var archive=new ZipArchive(file,ZipArchiveMode.Read))
            {
                if(archive.Entries.Count>1002)throw new InvalidDataException("The frame package contains too many files.");
                foreach(var entry in archive.Entries)
                {
                    if(String.IsNullOrEmpty(entry.Name))continue;
                    string name=SafeMember(entry.FullName);total+=entry.Length;
                    if(total>MaximumPackageBytes||entry.Length>MaximumImageBytes)throw new InvalidDataException("The expanded frame package is too large.");
                    using(var stream=entry.Open())files.Add(name,ReadBounded(stream,MaximumImageBytes));
                }
            }
            byte[] definition;if(!files.TryGetValue("frame.json",out definition)||definition.Length>2000000)throw new InvalidDataException("The package is missing a valid frame.json definition.");
            var frame=new JavaScriptSerializer{MaxJsonLength=2000000}.Deserialize<FrameDesign>(Encoding.UTF8.GetString(definition));FrameStore.Validate(frame);
            string directory=ImportDirectory();
            foreach(var decoration in frame.Elements.Where(e=>e.Kind=="Decoration"))
            {
                byte[] image;string name=SafeMember(decoration.ImagePath);
                if(!files.TryGetValue(name,out image))throw new InvalidDataException("A decoration image is missing from the frame package.");
                decoration.ImagePath=SaveImage(directory,image);
            }
            frame.Id=Guid.NewGuid().ToString("N");return frame;
        }
        static string SafeMember(string member)
        {
            if(String.IsNullOrWhiteSpace(member))throw new InvalidDataException("Invalid empty frame member name.");
            string normalized=member.Replace('\\','/');
            while(normalized.StartsWith("./",StringComparison.Ordinal))normalized=normalized.Substring(2);
            if(normalized.StartsWith("/",StringComparison.Ordinal)||normalized.Contains(":")||normalized.Split('/').Any(p=>p==".."||String.IsNullOrEmpty(p)))throw new InvalidDataException("Frame package paths must stay inside the package.");
            return normalized;
        }
        static void WriteEntry(ZipArchive archive,string name,byte[] bytes)
        {using(var stream=archive.CreateEntry(name,CompressionLevel.Optimal).Open())stream.Write(bytes,0,bytes.Length);}
        static byte[] ReadBounded(string path,int maximum)
        {using(var stream=File.OpenRead(path))return ReadBounded(stream,maximum);}
        static byte[] ReadBounded(Stream stream,int maximum)
        {
            using(var output=new MemoryStream())
            {
                byte[] buffer=new byte[32768];int count;
                while((count=stream.Read(buffer,0,buffer.Length))>0){if(output.Length+count>maximum)throw new InvalidDataException("The frame member is too large.");output.Write(buffer,0,count);}
                return output.ToArray();
            }
        }
        static byte[] ToPng(byte[] bytes)
        {
            using(var stream=new MemoryStream(bytes,false))using(var image=Image.FromStream(stream))
            {
                if((long)image.Width*image.Height>120000000)throw new InvalidDataException("The decoration exceeds 120 megapixels.");
                using(var output=new MemoryStream()){image.Save(output,ImageFormat.Png);return output.ToArray();}
            }
        }
        static string ImportDirectory()
        {string path=Path.Combine(FrameStore.Folder,"imports",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
        static string SaveImage(string directory,byte[] bytes)
        {string path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".png");File.WriteAllBytes(path,ToPng(bytes));return path;}

        // The recovered container uses little-endian TIFF-like 12-byte directory
        // entries. Tags 2001/4001 refer to embedded-file tables; tag 9999 links
        // directories. File records contain time, UTF-8 name/path and byte count.
        internal static Dictionary<string,byte[]> ReadPifMembers(byte[] data)
        {
            if(data.Length<132||Encoding.ASCII.GetString(data,0,5)!="EPPIF")throw new InvalidDataException("Not an EPPIF 3.1 container.");
            var members=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);var pending=new Queue<int>();var visited=new HashSet<int>();pending.Enqueue(118);
            while(pending.Count>0)
            {
                int offset=pending.Dequeue();if(offset==0||!visited.Add(offset))continue;
                ushort count=UInt16(data,offset);if(count>4096)throw new InvalidDataException("Invalid PIF directory size.");
                for(int n=0;n<count;n++)
                {
                    int entry=checked(offset+2+n*12);ushort tag=UInt16(data,entry),type=UInt16(data,entry+2);uint amount=UInt32(data,entry+4);int value=Offset(data,entry+8);
                    if(type!=4||amount!=1)continue;
                    if(tag==9999){if(value!=0)pending.Enqueue(value);}
                    else if(tag==2001||tag==4001)
                    {
                        ushort fileCount=UInt16(data,value);if(fileCount>1000)throw new InvalidDataException("Too many embedded PIF files.");
                        for(int f=0;f<fileCount;f++)ReadPifFile(data,Offset(data,value+2+f*4),members,new HashSet<int>());
                    }
                }
            }
            if(members.Count==0)throw new InvalidDataException("This PIF container version is not supported.");return members;
        }
        static void ReadPifFile(byte[] data,int offset,Dictionary<string,byte[]> members,HashSet<int> visited)
        {
                if(offset==0)return;
                if(!visited.Add(offset)||visited.Count>1000)throw new InvalidDataException("Cyclic PIF file tree.");
                int children=UInt16(data,offset);if(children>1000)throw new InvalidDataException("Too many nested PIF files.");
                int cursor=checked(offset+2+children*4+24);
                ushort nameLength=UInt16(data,cursor);cursor+=2;string name=Utf8(data,cursor,nameLength).TrimEnd('\0');cursor+=nameLength;
                ushort folderLength=UInt16(data,cursor);cursor+=2;string folder=Utf8(data,cursor,folderLength).TrimEnd('\0');cursor+=folderLength;
                int length=Offset(data,cursor);cursor+=4;Bounds(data,cursor,length);
                if(length>MaximumImageBytes)throw new InvalidDataException("The embedded PIF member is too large.");
                // PIF thumbnail records sometimes retain parent-relative authoring
                // paths. Names remain virtual dictionary keys; extraction always
                // writes a fresh GUID file, never an embedded path.
                string fullName=PifMember(folder.Replace('\\','/')+name);var bytes=new byte[length];Buffer.BlockCopy(data,cursor,bytes,0,length);
                if(members.ContainsKey(fullName))throw new InvalidDataException("Duplicate PIF member name.");members.Add(fullName,bytes);
                for(int child=0;child<children;child++)ReadPifFile(data,Offset(data,offset+2+child*4),members,visited);
        }
        static void Bounds(byte[] bytes,int offset,int length)
        {if(offset<0||length<0||offset>bytes.Length-length)throw new InvalidDataException("Truncated or invalid PIF record.");}
        static ushort UInt16(byte[] bytes,int offset){Bounds(bytes,offset,2);return BitConverter.ToUInt16(bytes,offset);}
        static uint UInt32(byte[] bytes,int offset){Bounds(bytes,offset,4);return BitConverter.ToUInt32(bytes,offset);}
        static int Offset(byte[] bytes,int offset){uint value=UInt32(bytes,offset);if(value>Int32.MaxValue)throw new InvalidDataException("Invalid PIF offset.");return (int)value;}
        static string Utf8(byte[] bytes,int offset,int length){Bounds(bytes,offset,length);return Encoding.UTF8.GetString(bytes,offset,length);}

        static FrameDesign ImportPif(byte[] data,string defaultName)
        {
            var files=ReadPifMembers(data);var xmlFile=files.FirstOrDefault(f=>f.Key.EndsWith(".xml",StringComparison.OrdinalIgnoreCase));
            if(xmlFile.Value==null)xmlFile=files.FirstOrDefault(f=>f.Value.Length>=5&&Encoding.UTF8.GetString(f.Value,0,Math.Min(16,f.Value.Length)).TrimStart('\uFEFF').StartsWith("<?xml",StringComparison.Ordinal));
            if(xmlFile.Value==null)throw new InvalidDataException("The PIF frame has no XML template.");
            var document=new XmlDocument{XmlResolver=null};
            using(var stream=new MemoryStream(xmlFile.Value,false))using(var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2000000}))document.Load(reader);
            var pageMasters=document.SelectNodes("//*[local-name()='simple-page-master']");
            if(pageMasters.Count!=1)throw new InvalidDataException("Only single-page PIF templates can currently be imported.");
            var cssFiles=files.Where(f=>f.Key.EndsWith(".css",StringComparison.OrdinalIgnoreCase)).ToList();if(cssFiles.Count!=1)throw new InvalidDataException("The PIF frame needs one embedded style sheet.");
            string css=Encoding.UTF8.GetString(cssFiles[0].Value),page=Regex.Match(css,@"@page\s*\{([^}]+)\}").Groups[1].Value;
            var size=Regex.Match(page,@"size\s*:\s*([\d.]+)px\s+([\d.]+)px");if(!size.Success)throw new InvalidDataException("The PIF page has no supported physical size.");
            var frame=new FrameDesign{Name=Meta(document,"DC.Title",defaultName),Paper=Meta(document,"PaperSize","Custom"),Category=Meta(document,"DC.Subject","Other"),Width=Number(size.Groups[1].Value),Height=Number(size.Groups[2].Value)};
            string resolution=Meta(document,"Resolution","360");if(resolution!="360")throw new InvalidDataException("Only 360-dpi PIF templates are currently supported.");
            var containers=new Dictionary<string,RectangleF>();float ml=CssPixels(page,"margin-left"),mt=CssPixels(page,"margin-top");
            foreach(Match match in Regex.Matches(css,@"\.(container_\d+)\s*\{([^}]+)\}"))
            {string rule=match.Groups[2].Value;containers[match.Groups[1].Value]=new RectangleF(ml+CssPixels(rule,"left"),mt+CssPixels(rule,"top"),CssPixels(rule,"width"),CssPixels(rule,"height"));}
            var indexes=new Dictionary<string,int>();string imageDirectory=null;int opening=0;
            foreach(XmlElement obj in pageMasters[0].SelectNodes(".//*[local-name()='object']"))
            {
                string type=obj.GetAttribute("type"),id=obj.GetAttribute("id"),cls=obj.GetAttribute("class");var parameters=Parameters(obj);RectangleF box;
                string container=AncestorContainer(obj);
                if(type=="application/x-pif-image-sequence"||type.StartsWith("image/",StringComparison.Ordinal))
                {
                    if(!containers.TryGetValue(container,out box)||box.Width<=0||box.Height<=0)throw new InvalidDataException("The PIF image placement is unsupported.");
                    var element=Element(type=="application/x-pif-image-sequence"?"Opening":"Decoration",box,frame);element.Rotation=ParameterNumber(parameters,"Rotation");
                    if(element.Kind=="Opening")
                    {indexes[id]=opening++;element.ImageGroup=obj.GetAttribute("name");element.ImageDirection=Parameter(parameters,"Direction");element.ImageRotation=(int)Math.Round(element.Rotation/90);element.Rotation=0;frame.Elements.Add(element);continue;}
                    string member=PifMember(obj.GetAttribute("data"));byte[] bytes;
                    if(!files.TryGetValue(member,out bytes))throw new InvalidDataException("The PIF decoration is missing from its container.");
                    if(type=="image/x-eff")bytes=KnownEff(bytes);
                    if(imageDirectory==null)imageDirectory=ImportDirectory();element.ImagePath=SaveImage(imageDirectory,bytes);frame.Elements.Add(element);
                }
                else if(type=="application/x-pif-text+xml")
                {
                    string captionClass=Regex.Match(cls,@"caption_\d+").Value;
                    string rule=Regex.Match(css,@"\."+Regex.Escape(captionClass)+@"\s*\{([^}]+)\}").Groups[1].Value;
                    float ox=ml,oy=mt;if(containers.TryGetValue(container,out box)){ox=box.X;oy=box.Y;}
                    var element=Element("Text",new RectangleF(ox+CssPixels(rule,"left"),oy+CssPixels(rule,"top"),CssPixels(rule,"width"),CssPixels(rule,"height")),frame);
                    if(element.Width<=0||element.Height<=0)throw new InvalidDataException("The PIF text placement is unsupported.");
                    string value=obj.Attributes.Cast<XmlAttribute>().Where(a=>a.LocalName=="caption-value").Select(a=>a.Value).FirstOrDefault()??"";
                    string marker=obj.InnerXml.Contains("EXIF_INFO")?"exif":obj.InnerXml.Contains("IMAGE_INFO")?"info":obj.InnerXml.Contains("COMMENT")?"comment":"";
                    element.Text=String.IsNullOrEmpty(marker)?obj.InnerText:value;element.CaptionRole=marker;element.CaptionValue=value;
                    int index;string target=Parameter(parameters,"Target").TrimStart('#');element.CaptionPhotoIndex=indexes.TryGetValue(target,out index)?index:0;
                    element.Points=Math.Max(1,Number(Regex.Match(rule,@"font-size\s*:\s*([\d.]+)pt").Groups[1].Value));
                    element.FontFamily=Regex.Match(rule,@"font-family\s*:\s*[""']?([^;""',]+)").Groups[1].Value; if(String.IsNullOrEmpty(element.FontFamily))element.FontFamily="Arial";
                    element.ColorArgb=CssColor(rule).ToArgb();element.Rotation=ParameterNumber(parameters,"StrDirection");element.VerticalText=Parameter(parameters,"ScriptDirection")=="Vertical";
                    element.TextAlignment=Parameter(parameters,"TextAlign");element.TextVerticalAlignment=Parameter(parameters,"VerticalAlign");frame.Elements.Add(element);
                }
                else if(type=="application/x-pif-line+xml")
                {
                    string[] points=Parameter(parameters,"PointList").Split(',');float thickness=Math.Max(.1f,ParameterNumber(parameters,"LineWidth"));
                    for(int n=0;n+3<points.Length;n+=2)
                    {
                        float x1=ml+Number(points[n]),y1=mt+Number(points[n+1]),x2=ml+Number(points[n+2]),y2=mt+Number(points[n+3]);
                        var element=Element("Line",new RectangleF(Math.Min(x1,x2),Math.Min(y1,y2),Math.Max(.001f,Math.Abs(x2-x1)),Math.Max(.001f,Math.Abs(y2-y1))),frame);
                        element.Thickness=thickness;element.ColorArgb=CsvColor(Parameter(parameters,"Color")).ToArgb();
                        element.ReverseLine=(x2-x1)*(y2-y1)<0;frame.Elements.Add(element);
                    }
                }
                else throw new InvalidDataException("The PIF frame contains an unsupported object type: "+type+".");
            }
            FrameStore.Validate(frame);return frame;
        }
        static byte[] KnownEff(byte[] bytes)
        {
            string digest;using(var sha=SHA256.Create())digest=String.Concat(sha.ComputeHash(bytes).Select(b=>b.ToString("x2",CultureInfo.InvariantCulture)));
            var known=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(AppAssets.ReadText("legacy-eff-masks.json"));string asset;
            if(!known.TryGetValue(digest,out asset))throw new InvalidDataException("This frame contains an unsupported EFF decoration. The four built-in white oval masks are supported; colored/custom EFF files require conversion to PNG.");
            using(var stream=AppAssets.Open(asset))return ReadBounded(stream,MaximumImageBytes);
        }
        static string PifMember(string value)
        {string result=value.Replace('\\','/');while(result.StartsWith("./",StringComparison.Ordinal))result=result.Substring(2);if(String.IsNullOrEmpty(result))throw new InvalidDataException("Empty PIF member name.");return result;}
        static FrameElement Element(string kind,RectangleF box,FrameDesign frame)
        {return new FrameElement{Kind=kind,X=box.X/frame.Width,Y=box.Y/frame.Height,Width=box.Width/frame.Width,Height=box.Height/frame.Height};}
        static string Meta(XmlDocument document,string name,string fallback)
        {foreach(XmlElement node in document.SelectNodes("//*[local-name()='DocMeta']/*[local-name()='meta']"))if(node.GetAttribute("name")==name)return node.GetAttribute("content");return fallback;}
        static Dictionary<string,string> Parameters(XmlElement obj)
        {var result=new Dictionary<string,string>();foreach(XmlElement node in obj.SelectNodes("./*[local-name()='param']"))result[node.GetAttribute("name")]=node.GetAttribute("value");return result;}
        static string Parameter(Dictionary<string,string> values,string name){string value;return values.TryGetValue(name,out value)?value:"";}
        static float ParameterNumber(Dictionary<string,string> values,string name){return Number(Parameter(values,name));}
        static string AncestorContainer(XmlElement element)
        {for(XmlNode node=element.ParentNode;node!=null;node=node.ParentNode){var parent=node as XmlElement;if(parent==null)continue;string value=Regex.Match(parent.GetAttribute("class"),@"\bcontainer_\d+\b").Value;if(value.Length>0)return value;}return "";}
        static float Number(string value){float n;return Single.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out n)?n:0;}
        static float CssPixels(string css,string name){return Number(Regex.Match(css,@"(?:^|[;\s])"+Regex.Escape(name)+@"\s*:\s*([\d.-]+)px").Groups[1].Value);}
        static Color CssColor(string rule){return CsvColor(Regex.Match(rule,@"color\s*:\s*rgb\(([^)]+)\)").Groups[1].Value);}
        static Color CsvColor(string value){var parts=value.Split(',');if(parts.Length<3)return Color.Black;return Color.FromArgb(Math.Max(0,Math.Min(255,(int)Number(parts[0]))),Math.Max(0,Math.Min(255,(int)Number(parts[1]))),Math.Max(0,Math.Min(255,(int)Number(parts[2]))));}
    }
}
