using System;
using System.Drawing.Printing;
using System.Drawing;
using System.Linq;

namespace FrameSpool
{
    // Uses the installed Windows driver's public capabilities. PICSDK's
    // private paper-media/edge-expansion fields are not available through GDI.
    public static class PrinterProfile
    {
        public static void Apply(PrintDocument document, PhotoLayout layout, PrintOptions options)
        {
            var page=document.DefaultPageSettings;
            bool nativeMode=DriverSettings.Apply(document,options);
            bool samePage=nativeMode&&options.DriverModePaper==options.Paper&&options.DriverModeLandscape==(layout.Width>layout.Height);
            if(!samePage)page.Landscape=layout.Width>layout.Height;
            int width=(int)Math.Round(Math.Min(layout.Width,layout.Height)*100/360),height=(int)Math.Round(Math.Max(layout.Width,layout.Height)*100/360);
            if(document.PrinterSettings.IsValid)
            {
                var paper=document.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p=>Math.Abs(p.Width-width)<=2&&Math.Abs(p.Height-height)<=2);
                if(!samePage)page.PaperSize=paper??new PaperSize(options.Paper,width,height);
                var source=document.PrinterSettings.PaperSources.Cast<PaperSource>().FirstOrDefault(p=>String.Equals(p.SourceName,options.Source,StringComparison.OrdinalIgnoreCase));
                if(source!=null)page.PaperSource=source;
                var resolutions=document.PrinterSettings.PrinterResolutions.Cast<PrinterResolution>().ToList();
                var resolution=resolutions.FirstOrDefault(r=>String.Equals(r.X>0&&r.Y>0?r.X+" x "+r.Y+" dpi":r.Kind.ToString(),options.Mode,StringComparison.OrdinalIgnoreCase));
                if(resolution==null && options.Mode!="Printer Properties")
                {
                    PrinterResolutionKind requested=options.Mode=="Draft"?PrinterResolutionKind.Draft:options.Mode=="Text"?PrinterResolutionKind.Low:options.Mode=="Text & Image"?PrinterResolutionKind.Medium:PrinterResolutionKind.High;
                    resolution=resolutions.FirstOrDefault(r=>r.Kind==requested);
                }
                if(resolution!=null && options.Mode!="Printer Properties")page.PrinterResolution=resolution;
            }
            else page.PaperSize=new PaperSize(options.Paper,width,height);
            page.Margins=new Margins(0,0,0,0);
        }
        public static void EnsurePageFormat(PrintDocument document,PhotoLayout layout,PrintOptions options)
        {
            var page=document.DefaultPageSettings;int width=(int)Math.Round(Math.Min(layout.Width,layout.Height)*100/360),height=(int)Math.Round(Math.Max(layout.Width,layout.Height)*100/360);
            if(Math.Abs(page.PaperSize.Width-width)>2||Math.Abs(page.PaperSize.Height-height)>2)
            {var paper=document.PrinterSettings.IsValid?document.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p=>Math.Abs(p.Width-width)<=2&&Math.Abs(p.Height-height)<=2):null;page.PaperSize=paper??new PaperSize(options.Paper,width,height);}
            page.Landscape=layout.Width>layout.Height;page.Margins=new Margins(0,0,0,0);
        }
        public static void DrawPrintedPage(PrintPageEventArgs page,PhotoLayout layout,PhotoPage content,PrintOptions options,bool corrected)
        {
            var g=page.Graphics;var saved=g.Save();
            try
            {
                // Render in the recovered 360-dpi coordinate system. A pixel font
                // otherwise becomes too small on a 600/720/1440-dpi printer when
                // PageBounds (hundredths of an inch) is used as the drawing size.
                g.PageUnit=GraphicsUnit.Pixel;
                g.TranslateTransform(-page.PageSettings.HardMarginX*g.DpiX/100,-page.PageSettings.HardMarginY*g.DpiY/100);
                g.ScaleTransform(page.PageBounds.Width*g.DpiX/100/layout.Width,page.PageBounds.Height*g.DpiY/100/layout.Height);
                LayoutEngine.DrawPage(g,new RectangleF(0,0,layout.Width,layout.Height),layout,content,options,corrected,null);
            }
            finally{g.Restore(saved);}
        }
    }
}
