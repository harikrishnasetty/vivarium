using System;
using System.Windows.Forms;

namespace Vivarium
{
    /*
     * Hidden top-level window used as a single-instance message target.
     * A second launch finds it by title and posts a command instead of
     * starting another wallpaper engine.
     */
    internal sealed class BridgeWindow : NativeWindow, IDisposable
    {
        public event Action OpenSettingsRequested;
        public event Action ExitRequested;
        public event Action NextRequested;

        public BridgeWindow()
        {
            CreateParams cp = new CreateParams();
            cp.Caption = "Vivarium.Bridge";
            cp.ClassName = "STATIC";
            cp.Style = 0;
            cp.ExStyle = 0;
            cp.X = 0;
            cp.Y = 0;
            cp.Width = 0;
            cp.Height = 0;
            cp.Parent = IntPtr.Zero;
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Interop.WM_VIVARIUM_SETTINGS)
            {
                Action handler = OpenSettingsRequested;
                if (handler != null) handler();
                return;
            }
            if (m.Msg == Interop.WM_VIVARIUM_EXIT)
            {
                Action handler = ExitRequested;
                if (handler != null) handler();
                return;
            }
            if (m.Msg == Interop.WM_VIVARIUM_NEXT)
            {
                Action handler = NextRequested;
                if (handler != null) handler();
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            try { DestroyHandle(); }
            catch { }
        }
    }
}
