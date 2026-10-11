using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Printing;
using System.Printing.Interop;
using System.Xml.Linq;

namespace FrameSpool
{
    public sealed class DriverChoice
    {
        public string Id, Label;
        internal XElement Option;
        public override string ToString(){return Label;}
    }
    // Print Schema preserves both standard and vendor option names. Never map
    // guessed marketing names to driver integers or transfer another queue's IDs.
    public sealed class DriverCapabilities
    {
        static readonly XNamespace Psf="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework";
        static readonly XNamespace Psk="http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords";
        readonly Dictionary<string,List<DriverChoice>> features=new Dictionary<string,List<DriverChoice>>();
        readonly Dictionary<string,string> selected=new Dictionary<string,string>();
        public string Error;
        public List<DriverChoice> Media{get{return Choices("PageMediaType");}}
        public List<DriverChoice> Quality{get{return Choices("PageOutputQuality");}}
        public List<DriverChoice> Resolution{get{return Choices("PageResolution");}}
        public bool SupportsBorderless{get{return Choices("PageBorderless").Any(c=>c.Id==Psk+"Borderless")&&Choices("PageBorderless").Any(c=>c.Id==Psk+"None");}}
        public bool BorderlessSelected{get{return Selected("PageBorderless")== (Psk+"Borderless").ToString();}}
        List<DriverChoice> Choices(string name){List<DriverChoice> value;return features.TryGetValue(name,out value)?value:new List<DriverChoice>();}
        public string Selected(string name){string value;return selected.TryGetValue(name,out value)?value:"";}
        static XName Qualified(XElement context,string value)
        {
            if(String.IsNullOrWhiteSpace(value))return XName.Get("Unnamed");
            int colon=value.IndexOf(':');if(colon<0)return context.GetDefaultNamespace()+value;
            return (context.GetNamespaceOfPrefix(value.Substring(0,colon))??XNamespace.None)+value.Substring(colon+1);
        }
        static string Label(XElement option,XName name)
        {
            var display=option.Elements().FirstOrDefault(e=>e.Attribute("name")!=null&&Qualified(e,(string)e.Attribute("name")).LocalName=="DisplayName");
            if(display!=null){string value=display.Descendants(Psf+"Value").Select(e=>e.Value).FirstOrDefault();if(!String.IsNullOrWhiteSpace(value))return value;}
            var dims=option.Elements(Psf+"ScoredProperty").Where(e=>new[]{"ResolutionX","ResolutionY"}.Contains(Qualified(e,(string)e.Attribute("name")).LocalName)).Select(e=>e.Descendants(Psf+"Value").Select(v=>v.Value).FirstOrDefault()).ToArray();
            if(dims.Length==2)return dims[0]+" x "+dims[1]+" dpi";
            return System.Text.RegularExpressions.Regex.Replace(name.LocalName,"([a-z])([A-Z])","$1 $2");
        }
        internal static DriverCapabilities Parse(Stream capabilities,Stream ticket)
        {
            var result=new DriverCapabilities();var xml=XDocument.Load(capabilities);
            foreach(var feature in xml.Root.Elements(Psf+"Feature"))
            {
                var featureName=Qualified(feature,(string)feature.Attribute("name"));
                if(featureName.Namespace!=Psk)continue;
                var choices=new List<DriverChoice>();
                foreach(var option in feature.Elements(Psf+"Option"))
                {
                    // Some resolutions have no name; preserve their scored properties.
                    var name=Qualified(option,(string)option.Attribute("name"));
                    var clone=new XElement(option);
                    foreach(var ns in option.AncestorsAndSelf().Reverse().SelectMany(e=>e.Attributes().Where(a=>a.IsNamespaceDeclaration)))clone.SetAttributeValue(ns.Name,ns.Value);
                    foreach(var property in clone.Elements(Psf+"Property").ToList())property.Remove();
                    foreach(var attribute in clone.DescendantsAndSelf().Attributes().Where(a=>a.Name.LocalName=="constrained").ToList())attribute.Remove();
                    string id=option.Attribute("name")==null?"resolution:"+String.Join("x",option.Descendants(Psf+"Value").Select(v=>v.Value)):name.ToString();
                    choices.Add(new DriverChoice{Id=id,Label=Label(option,name),Option=clone});
                }
                result.features[featureName.LocalName]=choices;
            }
            if(ticket!=null)
            {
                var current=XDocument.Load(ticket);
                foreach(var feature in current.Root.Elements(Psf+"Feature"))
                {
                    var featureName=Qualified(feature,(string)feature.Attribute("name"));var option=feature.Element(Psf+"Option");
                    if(featureName.Namespace==Psk&&option!=null)result.selected[featureName.LocalName]=option.Attribute("name")==null?"resolution:"+String.Join("x",option.Descendants(Psf+"Value").Select(v=>v.Value)):Qualified(option,(string)option.Attribute("name")).ToString();
                }
            }
            return result;
        }
        public static DriverCapabilities Read(PrintDocument document)
        {
            try
            {
                using(var server=new LocalPrintServer())using(var queue=server.GetPrintQueue(document.PrinterSettings.PrinterName))
                using(var converter=new PrintTicketConverter(document.PrinterSettings.PrinterName,1))
                {
                    var ticket=converter.ConvertDevModeToPrintTicket(DriverSettings.CurrentMode(document),PrintTicketScope.JobScope);
                    using(var cap=queue.GetPrintCapabilitiesAsXml(ticket))using(var stream=ticket.GetXmlStream())return Parse(cap,stream);
                }
            }
            catch(Exception ex){return new DriverCapabilities{Error=ex.Message};}
        }
        internal XDocument Delta(PrintOptions options)
        {
            var root=new XElement(Psf+"PrintTicket",new XAttribute("version","1"),new XAttribute(XNamespace.Xmlns+"psf",Psf),new XAttribute(XNamespace.Xmlns+"psk",Psk));
            Action<string,string> add=(feature,id)=>
            {
                var choice=Choices(feature).FirstOrDefault(c=>c.Id==id);if(choice==null)return;
                var option=new XElement(choice.Option);
                // The Windows provider expects QName prefix definitions at the ticket root.
                foreach(var declaration in option.Attributes().Where(a=>a.IsNamespaceDeclaration).ToList()){root.SetAttributeValue(declaration.Name,declaration.Value);declaration.Remove();}
                root.Add(new XElement(Psf+"Feature",new XAttribute("name","psk:"+feature),option));
            };
            add("PageMediaType",options.DriverMedia);add("PageOutputQuality",options.DriverQuality);add("PageResolution",options.DriverResolution);
            if(SupportsBorderless)add("PageBorderless",(Psk+(options.Borderless?"Borderless":"None")).ToString());
            return new XDocument(root);
        }
        public static void Apply(PrintDocument document,PrintOptions options)
        {
            if(!String.Equals(options.DriverOptionsPrinter,document.PrinterSettings.PrinterName,StringComparison.OrdinalIgnoreCase))return;
            var capabilities=Read(document);
            if(capabilities.Error!=null)throw new InvalidOperationException("The driver could not validate the saved print settings. Open Printer Settings and use Properties. "+capabilities.Error);
            if((!String.IsNullOrEmpty(options.DriverMedia)&&!capabilities.Media.Any(c=>c.Id==options.DriverMedia))||(!String.IsNullOrEmpty(options.DriverQuality)&&!capabilities.Quality.Any(c=>c.Id==options.DriverQuality))||(!String.IsNullOrEmpty(options.DriverResolution)&&!capabilities.Resolution.Any(c=>c.Id==options.DriverResolution))||options.Borderless&&!capabilities.SupportsBorderless)
                throw new InvalidOperationException("The saved print settings are no longer supported by this driver. Open Printer Settings and choose the available options.");
            var delta=capabilities.Delta(options);if(!delta.Root.HasElements)return;
            using(var server=new LocalPrintServer())using(var queue=server.GetPrintQueue(document.PrinterSettings.PrinterName))
            using(var converter=new PrintTicketConverter(document.PrinterSettings.PrinterName,1))using(var stream=new MemoryStream())
            {
                delta.Save(stream);stream.Position=0;
                var basis=converter.ConvertDevModeToPrintTicket(DriverSettings.CurrentMode(document),PrintTicketScope.JobScope);
                var requested=new PrintTicket(stream);
                var result=queue.MergeAndValidatePrintTicket(basis,requested,PrintTicketScope.JobScope);
                // ConflictResolved is a successful validation: the driver supplied a
                // viable combination. Keep that result instead of rejecting every job.
                if(result.ValidatedPrintTicket==null)throw new InvalidOperationException("The driver did not return validated printer settings. Use Printer Properties.");
                DriverSettings.SetMode(document,converter.ConvertPrintTicketToDevMode(result.ValidatedPrintTicket,BaseDevModeType.UserDefault,PrintTicketScope.JobScope));
            }
        }
    }
}
