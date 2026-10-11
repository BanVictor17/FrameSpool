using System;
using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FrameSpool
{
    // Standard Windows DEVMODE transport. In particular, this does not use the
    // old PICSDK whitelist and can pass an L8050 driver's private settings.
    public static class DriverSettings
    {
        [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr GlobalAlloc(uint flags,UIntPtr size);
        [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr GlobalLock(IntPtr handle);
        [DllImport("kernel32.dll")]static extern bool GlobalUnlock(IntPtr handle);
        [DllImport("kernel32.dll")]static extern IntPtr GlobalFree(IntPtr handle);
        [DllImport("kernel32.dll")]static extern UIntPtr GlobalSize(IntPtr handle);
        [DllImport("winspool.drv",EntryPoint="OpenPrinterW",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool OpenPrinter(string name,out IntPtr printer,IntPtr defaults);
        [DllImport("winspool.drv",SetLastError=true)]static extern bool ClosePrinter(IntPtr printer);
        [DllImport("winspool.drv",EntryPoint="DocumentPropertiesW",CharSet=CharSet.Unicode,SetLastError=true)]static extern int DocumentProperties(IntPtr window,IntPtr printer,string name,IntPtr output,IntPtr input,int mode);

        public static bool IsValidMode(byte[] bytes)
        {
            if(bytes==null||bytes.Length<72||bytes.Length>1048576)return false;
            int size=BitConverter.ToUInt16(bytes,68),extra=BitConverter.ToUInt16(bytes,70);
            return size>=72&&size+extra<=bytes.Length;
        }
        static byte[] ReadHandle(IntPtr handle)
        {
            long size=(long)GlobalSize(handle).ToUInt64();if(size<72||size>1048576)throw new InvalidOperationException("Invalid printer settings size.");
            IntPtr pointer=GlobalLock(handle);if(pointer==IntPtr.Zero)throw new Win32Exception();
            try{var bytes=new byte[(int)size];Marshal.Copy(pointer,bytes,0,bytes.Length);if(!IsValidMode(bytes))throw new InvalidOperationException("Invalid printer settings.");return bytes;}
            finally{GlobalUnlock(handle);}
        }
        static IntPtr WriteHandle(byte[] bytes)
        {
            if(!IsValidMode(bytes))throw new InvalidOperationException("Invalid printer settings.");
            IntPtr handle=GlobalAlloc(0x42,(UIntPtr)bytes.Length);if(handle==IntPtr.Zero)throw new OutOfMemoryException();IntPtr pointer=GlobalLock(handle);
            if(pointer==IntPtr.Zero){GlobalFree(handle);throw new Win32Exception();}
            try{Marshal.Copy(bytes,0,pointer,bytes.Length);}finally{GlobalUnlock(handle);}return handle;
        }
        static byte[] CurrentMode(PrintDocument document)
        {IntPtr handle=document.PrinterSettings.GetHdevmode(document.DefaultPageSettings);try{return ReadHandle(handle);}finally{GlobalFree(handle);}}
        public static bool Apply(PrintDocument document,PrintOptions options)
        {
            if(String.IsNullOrEmpty(options.DriverMode)||!String.Equals(options.DriverModePrinter,document.PrinterSettings.PrinterName,StringComparison.OrdinalIgnoreCase)||!document.PrinterSettings.IsValid)return false;
            try
            {
                byte[] bytes=Convert.FromBase64String(options.DriverMode),current=CurrentMode(document);
                // Driver version and structure sizes must still match; never transfer a
                // device-specific blob to another printer or a changed driver version.
                if(!IsValidMode(bytes)||BitConverter.ToUInt16(bytes,66)!=BitConverter.ToUInt16(current,66)||BitConverter.ToUInt16(bytes,68)!=BitConverter.ToUInt16(current,68)||BitConverter.ToUInt16(bytes,70)!=BitConverter.ToUInt16(current,70))return false;
                IntPtr handle=WriteHandle(bytes);try{document.PrinterSettings.SetHdevmode(handle);document.DefaultPageSettings.SetHdevmode(handle);}finally{GlobalFree(handle);}return true;
            }
            catch{return false;}
        }
        public static void Capture(PrintDocument document,PrintOptions options)
        {options.DriverMode=Convert.ToBase64String(CurrentMode(document));options.DriverModePrinter=document.PrinterSettings.PrinterName;options.DriverModePaper=options.Paper;options.DriverModeLandscape=document.DefaultPageSettings.Landscape;}
        public static bool Configure(IWin32Window owner,PhotoLayout layout,PrintOptions options)
        {
            using(var document=new PrintDocument())
            {
                document.PrinterSettings.PrinterName=options.Printer;
                if(!document.PrinterSettings.IsValid){MessageBox.Show(owner,"Install and select the printer's Windows driver first.","Printer Properties");return false;}
                PrinterProfile.Apply(document,layout,options);
                IntPtr printer;if(!OpenPrinter(options.Printer,out printer,IntPtr.Zero))throw new Win32Exception();
                try
                {
                    int size=DocumentProperties(owner.Handle,printer,options.Printer,IntPtr.Zero,IntPtr.Zero,0);
                    if(size<72||size>1048576)throw new InvalidOperationException("The driver did not provide valid printer settings.");
                    IntPtr buffer=Marshal.AllocHGlobal(size);
                    try
                    {
                        byte[] initial=CurrentMode(document);
                        if(initial.Length>size)throw new InvalidOperationException("Printer settings size changed.");Marshal.Copy(initial,0,buffer,initial.Length);
                        int result=DocumentProperties(owner.Handle,printer,options.Printer,buffer,buffer,14); // DM_IN_BUFFER|DM_OUT_BUFFER|DM_IN_PROMPT
                        if(result!=1)return false;
                        byte[] bytes=new byte[size];Marshal.Copy(buffer,bytes,0,size);
                        IntPtr handle=WriteHandle(bytes);try{document.PrinterSettings.SetHdevmode(handle);document.DefaultPageSettings.SetHdevmode(handle);}finally{GlobalFree(handle);}
                        Capture(document,options);
                        options.Source=document.DefaultPageSettings.PaperSource.SourceName;options.Mode="Printer Properties";options.PaperType="Printer Properties";
                        return true;
                    }
                    finally{Marshal.FreeHGlobal(buffer);}
                }
                finally{ClosePrinter(printer);}
            }
        }
    }
}
