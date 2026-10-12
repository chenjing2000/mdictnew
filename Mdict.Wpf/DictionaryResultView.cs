using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;
using Mdict.Application;
using Mdict.Wpf.WebView;

namespace Mdict.Wpf
{
    public sealed class DictionaryResultView : StackPanel, IDisposable
    {
        public string DictionaryPath { get; }
        public DictionaryService Service { get; } = new DictionaryService();
        public WebView2CompositionControl Browser { get; }
        public WebViewController Controller { get; }
        public bool IsAvailable { get; set; }
        private readonly Button collapseButton;
        private readonly System.Windows.Shapes.Path collapseArrow;
        private bool browserInitialized;
        private volatile bool disposed;

        public DictionaryResultView(string path)
        {
            DictionaryPath = path;
            Visibility = Visibility.Collapsed;
            Margin = new Thickness(0, 0, 0, 18);
            var header = new DockPanel();
            var arrowBrush = new SolidColorBrush(Color.FromRgb(184, 26, 53));
            collapseArrow = new System.Windows.Shapes.Path { Width = 16, Height = 16, Stretch = Stretch.Uniform,
                Fill = arrowBrush, Stroke = arrowBrush, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
            UpdateCollapseArrow(false);
            collapseButton = new Button { Content = collapseArrow, Width = 24, Height = 24,
                Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            collapseButton.Click += (_, _) =>
            {
                bool collapse = Browser.Visibility == Visibility.Visible;
                Browser.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
                UpdateCollapseButton(collapse);
            };
            DockPanel.SetDock(collapseButton, Dock.Left);
            header.Children.Add(collapseButton);
            header.Children.Add(new TextBlock { Text = path == null ? "WebView2" : Path.GetFileNameWithoutExtension(path),
                FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center });
            Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(224, 228, 232)),
                BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 12, 12, 10),
                Child = header });
            // Composition control participates in WPF clipping and the outer ScrollViewer.
            Browser = new WebView2CompositionControl { Height = 120, MinHeight = 80,
                DefaultBackgroundColor = System.Drawing.Color.White };
            Children.Add(Browser);
            Controller = new WebViewController(Browser, Service);
            Controller.ContentHeightChanged += height => Browser.Height = Math.Max(80, height);
        }

        public void ShowBrowser()
        {
            Visibility = Visibility.Visible;
            Browser.Visibility = Visibility.Visible;
            collapseButton.IsEnabled = true;
            UpdateCollapseButton(false);
        }

        public void PrepareResult()
        {
            Visibility = Visibility.Visible;
            // Keep the browser in layout for initialization, but never expose the old page.
            Browser.Visibility = Visibility.Hidden;
            collapseButton.IsEnabled = false;
        }

        public async System.Threading.Tasks.Task InitializeBrowserAsync()
        {
            if (browserInitialized) return;
            await Controller.InitializeAsync();
            browserInitialized = true;
        }

        private void UpdateCollapseButton(bool collapsed)
        {
            UpdateCollapseArrow(collapsed);
            collapseButton.ToolTip = collapsed ? "展开查询结果" : "收起查询结果";
            System.Windows.Automation.AutomationProperties.SetName(collapseButton, (string)collapseButton.ToolTip);
        }

        private void UpdateCollapseArrow(bool collapsed)
        {
            collapseArrow.Data = Geometry.Parse(collapsed ? "M 2,2 L 14,8 L 2,14 Z" : "M 2,2 L 14,2 L 8,14 Z");
        }

        public System.Threading.Tasks.Task OpenAsync()
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                Service.Open(DictionaryPath);
                // Closing can win the race before this background Open starts.
                if (disposed) Service.Close();
            });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            IsAvailable = false;
            Controller.Dispose();
            Browser.Dispose();
            // Close may wait for a reader; never block the UI on a prefix/resource lock.
            _ = System.Threading.Tasks.Task.Run(Service.Close);
        }
    }
}
