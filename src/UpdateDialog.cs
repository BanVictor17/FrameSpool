using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace FrameSpool
{
    public partial class MainForm
    {
        bool updateDialogOpen;
        void ShowUpdates()
        {
            if(updateDialogOpen)return;
            updateDialogOpen=true;
            try
            {
                using(var dialog=Ui.Dialog("FrameSpool Updates",480,235))
                using(var cancellation=new CancellationTokenSource())
                using(var client=AppUpdates.Client())
                {
                    var token=cancellation.Token;
                    bool installed=AppUpdates.IsInstalled(Environment.ProcessPath),busy=false;
                    AppUpdate available=null;string directory=null;
                    Ui.Label(dialog,"Current version: "+Application.ProductVersion+" · "+(installed?"Installed":"Portable"),18,16,440,22);
                    var status=Ui.Label(dialog,"Checking the latest GitHub release…",18,49,440,87);
                    var progress=new ProgressBar { Bounds=new Rectangle(18,141,440,18),Visible=false };dialog.Controls.Add(progress);
                    var action=Ui.Button(dialog,installed?"Install and restart":"Download and restart",18,180,175,30,()=>{});action.Enabled=false;
                    Ui.Button(dialog,"GitHub releases",201,180,135,30,()=>Process.Start(new ProcessStartInfo(AppUpdates.Repository+"/releases"){UseShellExecute=true}));
                    var close=Ui.Button(dialog,"Close",344,180,114,30,()=>dialog.Close());dialog.CancelButton=close;
                    dialog.FormClosing+=delegate{cancellation.Cancel();};
                    dialog.Shown+=async delegate
                    {
                        try
                        {
                            available=await AppUpdates.Check(client,installed,AppUpdates.ParseVersion(Application.ProductVersion),token);
                            if(dialog.IsDisposed||token.IsCancellationRequested)return;
                            status.Text=available==null?"You have the latest stable version.":"Version "+available.Version+" is available. The download will be verified against GitHub's SHA256 checksum.\r\n\r\nRestarting closes the current photo selection. Saved preferences and frames are kept.";
                            action.Enabled=available!=null;
                        }
                        catch(OperationCanceledException){if(!dialog.IsDisposed&&!token.IsCancellationRequested)status.Text="The update check timed out. Try again later.";}
                        catch(Exception ex){if(!dialog.IsDisposed)status.Text="Could not check for updates. "+ex.Message+"\r\nTry again later, or open GitHub releases.";}
                    };
                    action.Click+=async delegate
                    {
                        if(busy||available==null)return;busy=true;action.Enabled=false;close.Text="Cancel";progress.Visible=true;
                        directory=Path.Combine(AppIdentity.DataDirectory,"updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
                        try
                        {
                            status.Text="Downloading and verifying version "+available.Version+"…";
                            string payload=Path.Combine(directory,available.Name);
                            var updates=new Progress<int>(value=>{if(!progress.IsDisposed)progress.Value=value;});
                            await AppUpdates.Download(client,available,payload,updates,token);
                            token.ThrowIfCancellationRequested();
                            SavePreferences();
                            using(var parent=Process.GetCurrentProcess())
                                AppUpdates.LaunchHelper(directory,new UpdatePlan{Target=Environment.ProcessPath,Payload=payload,Hash=available.Hash,Version=available.Version,Installed=installed,ParentId=parent.Id,ParentStarted=parent.StartTime.ToUniversalTime().Ticks});
                            dialog.Close();Close();
                        }
                        catch(OperationCanceledException){ }
                        catch(Exception ex){if(!dialog.IsDisposed){status.Text="Update was not applied: "+ex.Message;action.Enabled=true;close.Text="Close";}}
                        finally{busy=false;}
                    };
                    dialog.ShowDialog(this);

                }
            }
            finally{updateDialogOpen=false;}
        }
    }
}
