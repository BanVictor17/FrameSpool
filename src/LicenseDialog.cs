using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using System.Drawing;

namespace FrameSpool
{
    internal static class LicenseDialog
    {
        public static void Show(IWin32Window owner)
        {
            using(var dialog=Ui.Dialog("FrameSpool Licenses",760,550))
            {
                var files=new ListBox{Bounds=new Rectangle(12,12,230,484)};
                var text=new TextBox{Bounds=new Rectangle(250,12,498,484),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=true};dialog.Controls.Add(files);dialog.Controls.Add(text);
                using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("FrameSpool.Runtime.zip"))using(var zip=new ZipArchive(resource,ZipArchiveMode.Read))
                foreach(var entry in zip.Entries)if(entry.FullName.StartsWith("notices/")&&!entry.FullName.EndsWith(".zip"))files.Items.Add(entry.FullName.Substring(8));
                files.SelectedIndexChanged+=delegate{using(var stream=AppRuntime.Open("notices/"+files.SelectedItem))using(var reader=new StreamReader(stream))text.Text=reader.ReadToEnd();};
                Ui.Button(dialog,"Save LibRaw Source...",12,512,190,25,delegate
                {
                    using(var save=new SaveFileDialog{FileName="LibRaw-0.22.2.zip",Filter="Source archive (*.zip)|*.zip"})
                    if(save.ShowDialog(dialog)==DialogResult.OK)using(var stream=AppRuntime.Open("notices/LibRaw/LibRaw-0.22.2.zip"))using(var destination=File.Create(save.FileName))stream.CopyTo(destination);
                });
                Ui.Button(dialog,"Close",648,512,100,25,delegate{dialog.Close();});if(files.Items.Count>0)files.SelectedIndex=0;dialog.ShowDialog(owner);
            }
        }
    }
}
