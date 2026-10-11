using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace FrameSpool
{
    internal static class DriverVerification
    {
        static void Check(List<VerificationCheck> checks,string name,bool passed,string detail=""){checks.Add(new VerificationCheck{Name=name,Passed=passed,Detail=detail});}
        public static void Run(string directory,List<VerificationCheck> checks)
        {
            const string ns="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework";
            const string keywords="http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords";
            string xml="<psf:PrintCapabilities xmlns:psf='"+ns+"' xmlns:k='"+keywords+"' xmlns:v='urn:fixture-driver' version='1'><psf:Feature name='k:PageMediaType'><psf:Option name='v:PhotoGlossy'><psf:Property name='psf:DisplayName'><psf:Value>Photo Glossy</psf:Value></psf:Property></psf:Option></psf:Feature><psf:Feature name='k:PageResolution'><psf:Option><psf:ScoredProperty name='k:ResolutionX'><psf:Value>600</psf:Value></psf:ScoredProperty><psf:ScoredProperty name='k:ResolutionY'><psf:Value>1200</psf:Value></psf:ScoredProperty></psf:Option></psf:Feature><psf:Feature name='k:PageBorderless'><psf:Option name='k:None'/><psf:Option name='k:Borderless'/></psf:Feature></psf:PrintCapabilities>";
            using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(xml)))
            {
                var capabilities=DriverCapabilities.Parse(stream,null);var options=new PrintOptions{DriverMedia="{urn:fixture-driver}PhotoGlossy",DriverResolution="resolution:600x1200",Borderless=true};
                Check(checks,"Driver options preserve vendor-qualified names",capabilities.Media.Count==1&&capabilities.Media[0].Id==options.DriverMedia&&capabilities.Media[0].Label=="Photo Glossy");
                Check(checks,"Driver resolutions read actual scored dimensions",capabilities.Resolution.Count==1&&capabilities.Resolution[0].Label=="600 x 1200 dpi");
                var delta=capabilities.Delta(options);var media=delta.Root.Elements(XName.Get("Feature",ns)).First();var option=media.Element(XName.Get("Option",ns));
                Check(checks,"Print Ticket preserves vendor namespaces without display-only properties",option.GetNamespaceOfPrefix("v")=="urn:fixture-driver"&&!option.Elements(XName.Get("Property",ns)).Any()&&capabilities.SupportsBorderless);
                delta.Save(Path.Combine(directory,"driver-vendor-ticket.xml"));
            }
            var printer=PrinterSettings.InstalledPrinters.Cast<string>().FirstOrDefault(n=>n=="Microsoft Print to PDF");
            if(printer==null)return;
            using(var document=new PrintDocument())
            {
                document.PrinterSettings.PrinterName=printer;var capabilities=DriverCapabilities.Read(document);
                Check(checks,"Installed Windows driver exposes Print Schema capabilities",capabilities.Error==null&&capabilities.Resolution.Count>0,capabilities.Error??printer);
                if(capabilities.Error!=null)return;
                var options=new PrintOptions{Printer=printer,Paper="A4",Borderless=false,DriverOptionsPrinter=printer,DriverResolution=capabilities.Resolution[0].Id};
                capabilities.Delta(options).Save(Path.Combine(directory,"driver-pdf-ticket.xml"));
                PrinterProfile.Apply(document,LayoutEngine.ForPaper("A4").First(),options);
                var applied=DriverCapabilities.Read(document);
                Check(checks,"Requested resolution survives validated Print Ticket and DEVMODE round-trip",applied.Selected("PageResolution")==options.DriverResolution);
                options.DriverMedia="{urn:removed-option}Unknown";bool rejected=false;
                try{DriverCapabilities.Apply(document,options);}catch(InvalidOperationException){rejected=true;}
                Check(checks,"Removed driver options are rejected before printing",rejected);
                DriverSettings.CaptureAccepted(document,options);
                Check(checks,"Accepted native settings clear obsolete Print Schema overrides",options.DriverOptionsPrinter==""&&options.DriverMedia==""&&options.DriverQuality==""&&options.DriverResolution=="");
                var path=Path.Combine(directory,"accepted-print-settings.json");WorkflowStorage.Save(path,options);
                var saved=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<PrintOptions>(File.ReadAllText(path));
                using(var restored=new PrintDocument())
                {
                    restored.PrinterSettings.PrinterName=printer;PrinterProfile.Apply(restored,LayoutEngine.ForPaper("A4").First(),saved);
                    Check(checks,"Persisted native settings retain the accepted resolution on the next job",DriverCapabilities.Read(restored).Selected("PageResolution")==applied.Selected("PageResolution")&&saved.DriverMode==options.DriverMode);
                }
            }
        }
    }
}
