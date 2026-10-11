using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FrameSpool
{
    public sealed class QuickPrintLaunch
    {
        public string Slot;
        public string[] Files = new string[0];
        public static QuickPrintLaunch Parse(string[] args)
        {
            var result = new QuickPrintLaunch();
            var files = new List<string>();
            for (int i = 0; i < (args ?? new string[0]).Length; i++)
            {
                string value = args[i];
                if (String.Equals(value, "--quick-print", StringComparison.OrdinalIgnoreCase))
                {
                    if (++i >= args.Length) throw new ArgumentException("Quick Print requires settings A or B.");
                    result.Slot = NormalizeSlot(args[i]);
                }
                else if (value.StartsWith("--quick-print=", StringComparison.OrdinalIgnoreCase))
                    result.Slot = NormalizeSlot(value.Substring(14));
                else if (File.Exists(value) || Directory.Exists(value)) files.Add(Path.GetFullPath(value));
            }
            result.Files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return result;
        }
        static string NormalizeSlot(string slot)
        {
            if (String.Equals(slot, "A", StringComparison.OrdinalIgnoreCase)) return "A";
            if (String.Equals(slot, "B", StringComparison.OrdinalIgnoreCase)) return "B";
            throw new ArgumentException("Quick Print requires settings A or B.");
        }
    }

    internal static class WorkflowStorage
    {
        // Write in the same directory so a failed/interrupted save preserves the previous file.
        public static void Save(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer { MaxJsonLength = 4000000 }.Serialize(value));
                AtomicFile.Commit(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal static class AtomicFile
    {
        public static void Commit(string temporary, string destination)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(destination)) File.Replace(temporary, destination, null);
                    else File.Move(temporary, destination);
                    return;
                }
                catch (IOException error)
                {
                    int code = error.HResult & 0xffff;
                    // Windows sharing/lock errors and ERROR_UNABLE_TO_REMOVE_REPLACED
                    // leave both files in place. A brief retry tolerates transient locks.
                    if (attempt >= 4 || (code != 32 && code != 33 && code != 1175) || !File.Exists(temporary)) throw;
                    System.Threading.Thread.Sleep(50 * (attempt + 1));
                }
            }
        }
    }

    internal static class QuickPrintShortcut
    {
        static string ExecutablePath
        {
            get
            {
#if NET5_0_OR_GREATER
                return Environment.ProcessPath ?? Application.ExecutablePath;
#else
                return Application.ExecutablePath;
#endif
            }
        }
        public static string DesktopPath(string slot)
        { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "FrameSpool Quick Print " + slot + ".lnk"); }
        static object Get(object target, string property)
        { return target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null); }
        static void Set(object target, string property, object value)
        { target.GetType().InvokeMember(property, BindingFlags.SetProperty, null, target, new[] { value }); }
        public static void Create(string path, string slot)
        {
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Set(shortcut, "TargetPath", ExecutablePath);
                Set(shortcut, "Arguments", "--quick-print " + slot);
                Set(shortcut, "WorkingDirectory", Path.GetDirectoryName(ExecutablePath));
                Set(shortcut, "Description", "Print photos with FrameSpool registered settings " + slot + ". Drop photos or folders here.");
                Set(shortcut, "IconLocation", ExecutablePath + ",0");
                shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        }
        public static bool IsRegisteredShortcut(string path, string slot)
        {
            if (!File.Exists(path)) return false;
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                return String.Equals(Convert.ToString(Get(shortcut, "Arguments")), "--quick-print " + slot, StringComparison.OrdinalIgnoreCase)
                    && String.Equals(Path.GetFileName(Convert.ToString(Get(shortcut, "TargetPath"))), Path.GetFileName(ExecutablePath), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
            finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        }
        public static void Remove(string slot)
        {
            string path = DesktopPath(slot);
            if (IsRegisteredShortcut(path, slot)) File.Delete(path);
        }
    }

    internal static class FolderShellIcon
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Info
        {
            public IntPtr Icon;
            public int Index;
            public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attributes, ref Info info, uint size, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        public static Icon Read(string path)
        {
            var info = new Info();
            try
            {
                // USEFILEATTRIBUTES avoids probing network/removable drives just to draw their icons.
                if (SHGetFileInfo(path, 0x10, ref info, (uint)Marshal.SizeOf(info), 0x100 | 0x1 | 0x10) == IntPtr.Zero || info.Icon == IntPtr.Zero) return null;
                using (var borrowed = Icon.FromHandle(info.Icon)) return (Icon)borrowed.Clone();
            }
            catch { return null; }
            finally { if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon); }
        }
    }

    internal sealed class StartupSplash : Form
    {
        readonly Timer timer = new Timer { Interval = 1200 };
        public StartupSplash(Form owner)
        {
            Owner = owner; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(340, 115);
            BackColor = Color.FromArgb(248, 250, 254);
            var title = new Label { Text = AppIdentity.Name, Font = new Font("Arial", 24, FontStyle.Bold), AutoSize = true, Location = new Point(27, 22) };
            Controls.Add(title); Controls.Add(new Label { Text = "Photo layouts, corrections and printing", AutoSize = true, Location = new Point(29, 73), Font = Ui.Font(13) });
            timer.Tick += delegate { Close(); };
            Shown += delegate { timer.Start(); };
            Click += delegate { Close(); }; title.Click += delegate { Close(); };
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
