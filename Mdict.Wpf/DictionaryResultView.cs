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
        private readonly TextBlock error;
        private volatile bool disposed;

        public DictionaryResultView(string path)
        {
            DictionaryPath = path;
            Margin = new Thickness(0, 0, 0, 18);
            Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(224, 228, 232)),
                BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 12, 12, 10),
                Child = new TextBlock { Text = path == null ? "WebView2" : Path.GetFileNameWithoutExtension(path),
                    FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap } });
            // Composition control participates in WPF clipping and the outer ScrollViewer.
            Browser = new WebView2CompositionControl { Height = 120, MinHeight = 80,
                DefaultBackgroundColor = System.Drawing.Color.White };
            Children.Add(Browser);
            error = new TextBlock { Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(186, 91, 73)), Visibility = Visibility.Collapsed };
            Children.Add(error);
            Controller = new WebViewController(Browser, Service);
            Controller.ContentHeightChanged += height => Browser.Height = Math.Max(80, height);
        }

        public void ShowError(string message)
        {
            Browser.Visibility = Visibility.Collapsed;
            error.Text = message;
            error.Visibility = Visibility.Visible;
        }

        public void ShowBrowser()
        {
            error.Visibility = Visibility.Collapsed;
            Browser.Visibility = Visibility.Visible;
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
