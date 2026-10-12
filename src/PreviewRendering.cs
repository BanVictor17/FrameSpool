using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FrameSpool
{
    internal sealed class BufferedPanel : Panel
    {
        public BufferedPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
    }

    // Bound decoding concurrency and coalesce rapid edits. Only the UI thread owns
    // displayed bitmaps; stale or closed-window results are disposed on completion.
    internal sealed class PreviewRenderer : IDisposable
    {
        static readonly SemaphoreSlim workers = new SemaphoreSlim(2);
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
        Func<Bitmap> pending;
        int version;
        bool busy;
        bool immediatePending;
        volatile bool disposed;
        public Bitmap Image { get; private set; }
        public string Error { get; private set; }
        public bool Ready { get { return !busy && pending == null && !timer.Enabled; } }
        public event Action Changed;
        public PreviewRenderer() { timer.Tick += delegate { timer.Stop(); Render(); }; }
        public void Request(Func<Bitmap> render, bool clear = false,bool immediate=false)
        {
            if (disposed) return;
            version++; pending = render; immediatePending=immediate;Error = null;
            if (clear && Image != null) { Image.Dispose(); Image = null; }
            timer.Stop();if(immediate)Render();else timer.Start();
        }
        public void Display(Bitmap image)
        {
            if(disposed){image.Dispose();return;}
            version++;pending=null;timer.Stop();Error=null;
            var previous=Image;Image=image;previous?.Dispose();Changed?.Invoke();
        }
        public static async Task<Bitmap> Decode(Func<Bitmap> render)
        {
            await workers.WaitAsync().ConfigureAwait(false);
            try { return await Task.Run(render).ConfigureAwait(false); }
            finally { workers.Release(); }
        }
        async void Render()
        {
            if (busy || disposed || pending == null) return;
            var render = pending; pending = null; int requested = version; busy = true;
            Bitmap result = null; string error = null;
            try { result = await Decode(()=>disposed||requested!=Volatile.Read(ref version)?null:render()); } catch (Exception ex) { error = ex.Message; }
            busy = false;
            if (disposed || requested != version) { if (result != null) result.Dispose(); }
            else
            {
                if (Image != null) Image.Dispose(); Image = result; Error = error;
                if (Changed != null) Changed();
            }
            if (!disposed && pending != null) { timer.Stop();if(immediatePending)Render();else timer.Start(); }
        }
        public void Dispose()
        {
            disposed = true; version++; pending = null; timer.Stop(); timer.Dispose();
            if (Image != null) { Image.Dispose(); Image = null; }
            Changed = null;
        }
    }
}
