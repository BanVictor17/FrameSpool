using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FrameSpool
{
    public sealed class WindowPlacement
    {
        public int X,Y,Width,Height,Dpi=96;
        public bool Maximized;
        public string Monitor;
    }
    public partial class MainForm
    {
        bool restoringWindow,windowLoaded;
        FormWindowState lastWindowState=FormWindowState.Normal;
        Rectangle normalWindowBounds;
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);RestoreWindowPlacement();windowLoaded=true;TrackWindowPlacement();
        }
        void TrackWindowPlacement()
        {
            if(!windowLoaded||restoringWindow)return;
            if(WindowState!=FormWindowState.Minimized)lastWindowState=WindowState;
            if(WindowState==FormWindowState.Normal)normalWindowBounds=Bounds;
        }
        internal static Rectangle VisibleWindowBounds(Rectangle desired,Rectangle working,Size minimum)
        {
            int width=Math.Min(working.Width,Math.Max(minimum.Width,desired.Width));
            int height=Math.Min(working.Height,Math.Max(minimum.Height,desired.Height));
            return new Rectangle(Math.Max(working.Left,Math.Min(desired.X,working.Right-width)),
                Math.Max(working.Top,Math.Min(desired.Y,working.Bottom-height)),width,height);
        }
        void RestoreWindowPlacement()
        {
            restoringWindow=true;
            try
            {
                string path=settingsFile+".window";if(!File.Exists(path))return;
                var saved=new JavaScriptSerializer().Deserialize<WindowPlacement>(File.ReadAllText(path));
                if(saved==null||saved.Width<=0||saved.Height<=0||saved.Width>32768||saved.Height>32768)return;
                var desired=new Rectangle(saved.X,saved.Y,saved.Width,saved.Height);
                var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==saved.Monitor)??Screen.FromRectangle(desired);
                StartPosition=FormStartPosition.Manual;
                Location=VisibleWindowBounds(desired,screen.WorkingArea,MinimumSize).Location;
                double ratio=UiScale*96/Math.Max(48,Math.Min(768,saved.Dpi));
                desired.Width=(int)Math.Round(saved.Width*ratio);desired.Height=(int)Math.Round(saved.Height*ratio);
                MinimumSize=new Size(Math.Min(Pixels(800),screen.WorkingArea.Width),Math.Min(Pixels(600),screen.WorkingArea.Height));
                Bounds=VisibleWindowBounds(desired,screen.WorkingArea,MinimumSize);normalWindowBounds=Bounds;
                lastWindowState=saved.Maximized?FormWindowState.Maximized:FormWindowState.Normal;
                WindowState=lastWindowState;
            }
            catch(Exception error)when(error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is InvalidOperationException||error is System.Text.Json.JsonException){ }
            finally{restoringWindow=false;}
        }
        void SaveWindowPlacement()
        {
            if(!windowLoaded)return;
            try
            {
                var bounds=WindowState==FormWindowState.Normal?Bounds:RestoreBounds;
                if(bounds.Width<=0||bounds.Height<=0)bounds=normalWindowBounds;
                if(bounds.Width<=0||bounds.Height<=0)return;
                var screen=Screen.FromControl(this);
                bounds=VisibleWindowBounds(bounds,screen.WorkingArea,MinimumSize);
                WorkflowStorage.Save(settingsFile+".window",new WindowPlacement{X=bounds.X,Y=bounds.Y,Width=bounds.Width,Height=bounds.Height,
                    Dpi=(int)Math.Round(UiScale*96),Monitor=screen.DeviceName,
                    Maximized=(WindowState==FormWindowState.Minimized?lastWindowState:WindowState)==FormWindowState.Maximized});
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
