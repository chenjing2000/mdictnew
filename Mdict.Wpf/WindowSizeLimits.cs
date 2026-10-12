using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Mdict.Wpf
{
    internal static class WindowSizeLimits
    {
        public static void Attach(Window window, double maxWidth, double maxHeight)
        {
            window.MaxWidth = maxWidth;
            window.MaxHeight = maxHeight;
            var maximum = new Size(maxWidth, maxHeight);
            var minimum = new Size(window.MinWidth, window.MinHeight);
            window.SourceInitialized += (_, _) =>
            {
                var source = (HwndSource)PresentationSource.FromVisual(window);
                HwndSourceHook workAreaChanged = (IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    // WM_NCLBUTTONDOWN / HTSYSMENU: keep the icon, suppress its click menu.
                    if (message == 0x00A1 && wParam.ToInt64() == 3) handled = true;
                    // WM_SETTINGCHANGE / WM_DISPLAYCHANGE also cover taskbar and display changes.
                    if (message == 0x001A || message == 0x007E) Update(window, maximum, minimum);
                    return IntPtr.Zero;
                };
                source.AddHook(workAreaChanged);
                window.Closed += (_, _) => { if (!source.IsDisposed) source.RemoveHook(workAreaChanged); };
                Update(window, maximum, minimum);
            };
            window.LocationChanged += (_, _) => Update(window, maximum, minimum);
            window.DpiChanged += (_, _) => Update(window, maximum, minimum);
        }

        private static void Update(Window window, Size maximum, Size minimum)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            double width = SystemParameters.WorkArea.Width;
            double height = SystemParameters.WorkArea.Height;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(MonitorFromWindow(handle, 2), ref info))
            {
                // Native monitor bounds are physical pixels; WPF dimensions are DIPs.
                var scale = HwndSource.FromHwnd(handle).CompositionTarget.TransformFromDevice;
                width = (info.Work.Right - info.Work.Left) * scale.M11;
                height = (info.Work.Bottom - info.Work.Top) * scale.M22;
            }
            window.MaxWidth = Math.Min(maximum.Width, width);
            window.MaxHeight = Math.Min(maximum.Height, height);
            window.MinWidth = Math.Min(minimum.Width, window.MaxWidth);
            window.MinHeight = Math.Min(minimum.Height, window.MaxHeight);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rectangle { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rectangle Monitor;
            public Rectangle Work;
            public int Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }
}
