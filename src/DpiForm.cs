using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FrameSpool
{
    // Layout definitions remain in 96-DPI units. Rebuild from those definitions,
    // never from already rounded/scaled coordinates when moving between monitors.
    public class DpiForm : Form
    {
        sealed class Definition { public Rectangle Bounds; public Font Font,ScaledFont; public int ItemHeight; }
        readonly Dictionary<Control,Definition> definitions=new Dictionary<Control,Definition>();
        readonly List<Font> ownedFonts=new List<Font>();
        float factor=1;
        Size logicalMinimum;
        bool loaded;
        public float UiScale { get { return factor; } }
        public int Pixels(int value) { return (int)Math.Round(value*factor); }
        public Rectangle Scaled(Rectangle value) { return new Rectangle(Pixels(value.X),Pixels(value.Y),Pixels(value.Width),Pixels(value.Height)); }
        public Point Scaled(Point value) { return new Point(Pixels(value.X),Pixels(value.Y)); }
        protected override void OnLoad(EventArgs e)
        {
            logicalMinimum=MinimumSize;var original=ClientSize;loaded=true;
            ApplyDpi(DeviceDpi);ClientSize=new Size(Pixels(original.Width),Pixels(original.Height));
            var working=Screen.FromControl(this).WorkingArea;
            if(Width>working.Width||Height>working.Height)
            {
                if(this is not MainForm){AutoScroll=true;AutoScrollMinSize=ClientSize;}
                Size=new Size(Math.Min(Width,working.Width),Math.Min(Height,working.Height));
            }
            base.OnLoad(e);
        }
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);if(!loaded)return;
            if(this is not MainForm&&AutoScroll){AutoScrollPosition=Point.Empty;AutoScrollMinSize=Size.Empty;}
            ApplyDpi(e.DeviceDpiNew);
            if(WindowState==FormWindowState.Normal)
            {
                var working=Screen.FromControl(this).WorkingArea;
                if(Width>working.Width||Height>working.Height)
                {if(this is not MainForm)AutoScroll=true;Size=new Size(Math.Min(Width,working.Width),Math.Min(Height,working.Height));}
                Location=new Point(Math.Max(working.Left,Math.Min(Left,working.Right-Width)),Math.Max(working.Top,Math.Min(Top,working.Bottom-Height)));
            }
        }
        protected void ScaleNewControls() { ApplyDpi((int)Math.Round(factor*96)); }
        internal void ApplyDpi(int dpi)
        {
            SuspendLayout();
            foreach(var control in definitions.Keys.Where(c=>c.IsDisposed).ToArray()) { definitions[control].Font?.Dispose();definitions.Remove(control); }
            CaptureDefinition(this);factor=dpi/96f;
            foreach(var pair in definitions)
            {
                var c=pair.Key;var d=pair.Value;
                if(d.Font!=null)
                {
                    if(d.ScaledFont==null||Math.Abs(d.ScaledFont.Size-d.Font.Size*factor)>.01)
                    {
                        d.ScaledFont=ownedFonts.FirstOrDefault(font=>font.FontFamily.Name==d.Font.FontFamily.Name&&font.Style==d.Font.Style&&font.Unit==d.Font.Unit&&Math.Abs(font.Size-d.Font.Size*factor)<.01);
                        if(d.ScaledFont==null){d.ScaledFont=new Font(d.Font.FontFamily,d.Font.Size*factor,d.Font.Style,d.Font.Unit);ownedFonts.Add(d.ScaledFont);}
                    }
                    c.Font=d.ScaledFont;
                }
                if(c==this)continue;
                if(c.Dock==DockStyle.None)c.Bounds=Scaled(d.Bounds);
                else if(c is MenuStrip)c.Height=Pixels(d.Bounds.Height);
                if(c is ListBox list&&d.ItemHeight>0)list.ItemHeight=Pixels(d.ItemHeight);
            }
            if(logicalMinimum!=Size.Empty)
            {var working=Screen.FromControl(this).WorkingArea;MinimumSize=new Size(Math.Min(Pixels(logicalMinimum.Width),working.Width),Math.Min(Pixels(logicalMinimum.Height),working.Height));}
            ResumeLayout(true);OnUiScaleChanged();Invalidate(true);
        }
        void CaptureDefinition(Control control)
        {
            if(!definitions.ContainsKey(control))
            {
                var property=TypeDescriptor.GetProperties(control)["Font"];
                definitions[control]=new Definition { Bounds=control.Bounds,
                    Font=control==this||property!=null&&property.ShouldSerializeValue(control)?(Font)control.Font.Clone():null,
                    ItemHeight=control is ListBox list?list.ItemHeight:0 };
            }
            foreach(Control child in control.Controls)CaptureDefinition(child);
        }
        protected virtual void OnUiScaleChanged() { }
        protected override void Dispose(bool disposing)
        {base.Dispose(disposing);if(disposing){foreach(var d in definitions.Values)d.Font?.Dispose();definitions.Clear();foreach(var font in ownedFonts)font.Dispose();ownedFonts.Clear();}}
    }
}
