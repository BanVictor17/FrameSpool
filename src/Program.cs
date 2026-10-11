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
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException+=delegate(object s,System.Threading.ThreadExceptionEventArgs e){ReportError(e.Exception);};
            try
            {
                if(args.Length>0&&args[0]=="--self-test")return Verification.Run(args.Length>1?args[1]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification"));
                if(args.Length>0&&args[0]=="--print-test")return Verification.PrintPdfTest(args.Length>1?args[1]:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verification"));
                Application.Run(new MainForm(args.Where(File.Exists).ToArray()));return 0;
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
