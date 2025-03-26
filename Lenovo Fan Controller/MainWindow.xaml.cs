using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Lenovo_Fan_Controller
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();
            SetWindowProperties();
        }

        private void SetWindowProperties()
        {
            // Center window on screen
            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var width = 800;
            var height = 650;
            appWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                (displayArea.WorkArea.Width - width) / 2,
                (displayArea.WorkArea.Height - height) / 2,
                width,
                height));
        }
    }
}