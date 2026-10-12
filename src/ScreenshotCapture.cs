using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class MainForm
    {
        // Reproducible documentation captures of our own forms, in-process.
        internal static int CaptureScreenshots(string directory,string sample)
        {
            Directory.CreateDirectory(directory);
            using(var form=new MainForm(new[]{sample},Path.Combine(directory,"capture-preferences.json")))
            {
                form.ShowInTaskbar=false;form.Opacity=0;form.Size=new Size(1040,720);form.Show();Application.DoEvents();form.AllPhotos.Clear();
                var first=new PhotoItem(sample){Copies=6,Comment="Blue Marble"};first.Adjustments.Mode="None";
                var second=new PhotoItem(sample){Copies=2};second.Rotation=1;second.Adjustments.Mode="None";
                form.AllPhotos.AddRange(new[]{first,second});form.ShowFolderPhotos(form.AllPhotos);form.Focus(first);form.RefreshSelection();
                form.folders.Nodes.Clear();var root=form.folders.Nodes.Add("Demo Photos");root.Tag=Path.GetDirectoryName(sample);root.Nodes.Add("Earth").Tag=root.Tag;root.Expand();
                form.currentFolder="Demo Photos";form.UpdateExplorerNavigation();
                SaveSurface(form,Path.Combine(directory,"select-photos.png"));
                form.Options.Paper="A4";form.Options.PaperType=form.Options.Mode="Printer Properties";form.Options.Borderless=false;
                form.CurrentLayout=FrameSpool.LayoutEngine.ForPaper("A4").First(l=>l.Frames.Count==8&&!l.Id.StartsWith("bo"));form.BuildPrinting();form.BuildMenus();form.PerformLayout();
                SaveSurface(form,Path.Combine(directory,"layouts.png"));form.Focus(first);
                CaptureDialog("Print Correction",directory,"corrections.png",delegate{form.ShowCorrection();});
                CaptureDialog("Printer Settings",directory,"printer-settings.png",delegate{form.ShowPrinterSettings();});
            }
            var design=new FrameDesign{Name="Blue Marble Frame",Width=1800,Height=1260,Paper="3.5x5"};
            design.Elements.Add(new FrameElement{Kind="Ellipse Opening",X=.08f,Y=.06f,Width=.84f,Height=.75f});
            design.Elements.Add(new FrameElement{Kind="Text",Text="BLUE MARBLE",X=.08f,Y=.84f,Width=.84f,Height=.1f,Points=18});
            using(var editor=new FrameEditor(design)){editor.ShowInTaskbar=false;editor.Opacity=0;editor.Show();Application.DoEvents();SaveSurface(editor,Path.Combine(directory,"frame-editor.png"));}
            return 0;
        }
        static void CaptureDialog(string title,string directory,string file,Action show)
        {
            bool captured=false;
            using(var timer=new Timer{Interval=80})
            {
                timer.Tick+=delegate
                {
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text==title);
                    if(dialog==null)return;timer.Stop();dialog.Opacity=0;
                    foreach(var label in Descendants(dialog).OfType<Label>().Where(l=>l.Text.StartsWith("Location:")))label.Text="Location: Demo Photos";
                    SaveSurface(dialog,Path.Combine(directory,file));captured=true;dialog.DialogResult=DialogResult.Cancel;dialog.Close();
                };
                timer.Start();show();timer.Stop();
            }
            if(!captured)throw new InvalidOperationException("Could not capture "+title);
        }
        static IEnumerable<Control> Descendants(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var descendant in Descendants(child))yield return descendant;}}
    }
}
