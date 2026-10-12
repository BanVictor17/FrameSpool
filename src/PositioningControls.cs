using System;
using System.Drawing;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class MainForm
    {
        Button inlineTrim,inlineFit;
        TrackBar inlineZoom;
        Label inlineZoomValue;
        bool syncingInline;
        Action syncPositioningPalette;
        internal static Point PositioningLocation(Rectangle owner,Size palette,Rectangle working,int topInset)
        {return new Point(Math.Max(working.Left,Math.Min(owner.Left,working.Right-palette.Width)),Math.Max(working.Top,Math.Min(owner.Top+topInset,working.Bottom-palette.Height)));}
        void BuildInlinePositioning()
        {
            inlineTrim=Ui.Button(body,"Trim",240,0,58,25,()=>SetPhotoFit(false));
            inlineFit=Ui.Button(body,"Fit",304,0,58,25,()=>SetPhotoFit(true));
            inlineZoom= new TrackBar{AutoSize=false,Bounds=new Rectangle(369,0,142,28),Minimum=50,Maximum=200,Value=100,TickStyle=TickStyle.None,SmallChange=1,LargeChange=10};
            inlineZoom.AccessibleName="Photo magnification";body.Controls.Add(inlineZoom);
            inlineZoomValue=Ui.Label(body,"100%",517,0,62,25);inlineZoomValue.TextAlign=ContentAlignment.MiddleLeft;
            inlineZoom.ValueChanged+=delegate{if(!syncingInline)SetPhotoZoom(inlineZoom.Value);};
            tips.SetToolTip(inlineTrim,"Fill the frame, cropping edges. Resets magnification and offsets.");
            tips.SetToolTip(inlineFit,"Fit the entire photo inside the frame. Resets magnification and offsets.");
            tips.SetToolTip(inlineZoom,"Magnification of the selected photo, 50–200%.");
            SyncPositioningControls();
        }
        void SetPhotoFit(bool fit)
        {
            if(FocusPhoto==null)return;
            FocusPhoto.Fit=fit;FocusPhoto.Zoom=1;FocusPhoto.OffsetX=FocusPhoto.OffsetY=0;UpdatePage();
        }
        void SetPhotoZoom(int value)
        {if(FocusPhoto==null)return;FocusPhoto.Zoom=Math.Max(50,Math.Min(200,value))/100f;UpdatePage();}
        void SyncPositioningControls()
        {
            if(inlineZoom!=null&&!inlineZoom.IsDisposed)
            {
                syncingInline=true;
                try
                {
                    bool enabled=printingScreen&&FocusPhoto!=null;
                    inlineTrim.Enabled=inlineFit.Enabled=inlineZoom.Enabled=inlineZoomValue.Enabled=enabled;
                    inlineZoom.Value=enabled?(int)Math.Round(FocusPhoto.Zoom*100):100;
                    inlineZoomValue.Text=enabled?inlineZoom.Value+"%":"—";
                    inlineTrim.FlatStyle=inlineFit.FlatStyle=FlatStyle.Standard;
                    inlineTrim.BackColor=enabled&&!FocusPhoto.Fit?Color.LightSteelBlue:SystemColors.Control;
                    inlineFit.BackColor=enabled&&FocusPhoto.Fit?Color.LightSteelBlue:SystemColors.Control;
                }
                finally{syncingInline=false;}
            }
            syncPositioningPalette?.Invoke();
        }
    }
}
