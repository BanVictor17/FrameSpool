using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FrameSpool
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException+=delegate(object s,System.Threading.ThreadExceptionEventArgs e){ReportError(e.Exception);};
            try
            {
                if(args.Length==2&&args[0]=="--apply-update")return AppUpdates.Apply(Path.GetFullPath(args[1]));
                if(args.Length>0&&args[0]=="--self-test")return Verification.Run(args.Length>1?args[1]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification"));
                if(args.Length>0&&args[0]=="--print-test")return Verification.PrintPdfTest(args.Length>1?args[1]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification"));
                if(args.Length==3&&args[0]=="--capture-screenshots")return MainForm.CaptureScreenshots(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]));
                var launch=QuickPrintLaunch.Parse(args);
                var form=new MainForm(launch.Files);
                form.Shown+=delegate{if(launch.Slot!=null)form.StartQuickPrint(launch.Slot);};
                Application.Run(form);return 0;
            }
            catch(Exception ex){ReportError(ex);return 1;}
        }
        static void ReportError(Exception error)
        {
            try { var folder=AppIdentity.DataDirectory;Directory.CreateDirectory(folder);File.AppendAllText(Path.Combine(folder,"errors.log"),DateTime.Now.ToString("s")+"\r\n"+error+"\r\n"); } catch { }
            MessageBox.Show(error.Message,"FrameSpool",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }
}
