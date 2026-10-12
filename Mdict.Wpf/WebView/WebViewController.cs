using System;
using System.Globalization;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Mdict.Application;

namespace Mdict.Wpf.WebView
{
    // Browser navigation and display only; no MDX/MDD format knowledge.
    public class WebViewController : IDisposable
    {
        public const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/#download-section";
        private readonly IWebView2 view;
        private readonly DictionaryService dictionaryService;
        private WebResourceHandler resources;
        private bool initialized;
        private bool navigationBusy;
        private string permittedUrl = "";
        private ulong navigationId;
        private bool disposed;
        private TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs> pendingNavigation;
        private double contentHeight;
        private readonly DependencyPropertyDescriptor zoomProperty;

        // A requested entry key is delivered to the UI, not looked up here.
        public event Action<string> EntryLookupRequested;
        public event Action<string, bool> TextSelectionRequested;
        public event Action<double> ContentHeightChanged;
        public event Action<double> ScrollRequested;
        public event Action<double> AnchorRequested;

        public WebViewController(IWebView2 webView, DictionaryService service)
        {
            view = webView;
            dictionaryService = service;
            zoomProperty = DependencyPropertyDescriptor.FromName("ZoomFactor", view.GetType(), view.GetType());
            zoomProperty?.AddValueChanged(view, OnZoomFactorChanged);
        }

        public async Task InitializeAsync()
        {
            string runtimeError = GetRuntimeError();
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            string cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MdictViewer", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: cachePath);
            if (disposed) throw new ObjectDisposedException(nameof(WebViewController));
            await view.EnsureCoreWebView2Async(environment);
            if (disposed) throw new ObjectDisposedException(nameof(WebViewController));
            CoreWebView2 core = view.CoreWebView2;
            // Developer diagnostic switch only; Oxford scripts enabled by default.
            bool disableScripts = Environment.GetEnvironmentVariable("MDICT_DISABLE_SCRIPTS") == "1";
            core.Settings.IsScriptEnabled = !disableScripts;
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
#if DEBUG
            core.Settings.AreDevToolsEnabled = true;
#else
            core.Settings.AreDevToolsEnabled = false;
#endif
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.PermissionRequested += OnPermissionRequested;
            core.WebMessageReceived += OnWebMessageReceived;
            view.ZoomFactorChanged += OnZoomFactorChanged;
            resources = new WebResourceHandler(core, dictionaryService);
            resources.Register();
            if (!disableScripts)
            {
                // Register all bridges before the first document.
                await RegisterEmbeddedScriptAsync(core, "AudioBridge.js");
                await RegisterEmbeddedScriptAsync(core, "EntryBridge.js");
                await RegisterEmbeddedScriptAsync(core, "LayoutBridge.js");
            }
            initialized = true;
        }

        public static string GetRuntimeError()
        {
            try
            {
                CoreWebView2Environment.GetAvailableBrowserVersionString();
                return null;
            }
            catch (WebView2RuntimeNotFoundException)
            {
                return "Install Microsoft Edge WebView2 Runtime and try again. Download: " + RuntimeDownloadUrl;
            }
        }

        private static async Task RegisterEmbeddedScriptAsync(CoreWebView2 core, string filename)
        {
            Assembly assembly = typeof(WebViewController).Assembly;
            string resourceName = "Mdict.Wpf.WebView." + filename;
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("缺少嵌入资源：" + filename);
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string script = await reader.ReadToEndAsync();
                    await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
                }
            }
        }

        // Recognize only our own internal /lookup route. Decode once and do
        // not use WebUtility.UrlDecode (which would turn '+' into a space).
        private static bool TryReadLookupKey(string url, out string key)
        {
            key = "";
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, "mdict.invalid", StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort || uri.AbsolutePath != "/lookup" ||
                uri.Fragment.Length != 0 ||
                !uri.Query.StartsWith("?key=", StringComparison.Ordinal))
                return false;

            string encoded = uri.Query.Substring(5);
            if (encoded.Length == 0 || encoded.Length > 3072 || encoded.Contains('&'))
                return false;
            for (int i = 0; i < encoded.Length; i++)
            {
                if (encoded[i] != '%')
                    continue;
                if (i + 2 >= encoded.Length || !Uri.IsHexDigit(encoded[i + 1]) ||
                    !Uri.IsHexDigit(encoded[i + 2]))
                    return false;
                i += 2;
            }
            try
            {
                key = Uri.UnescapeDataString(encoded);
            }
            catch (UriFormatException)
            {
                return false;
            }
            if (key.Length == 0 || key.Length > 512)
                return false;
            foreach (char character in key)
            {
                if (char.IsControl(character))
                    return false;
            }
            return true;
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            string key;
            if (TryReadLookupKey(e.Uri, out key))
            {
                // Cancel synchronously, but never start a new navigation inside
                // NavigationStarting: it would cancel the active navigation ID.
                e.Cancel = true;
                ((FrameworkElement)view).Dispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(() =>
                    {
                        if (!disposed && !navigationBusy)
                            EntryLookupRequested?.Invoke(key);
                    }));
                return;
            }

            bool permitted = navigationBusy &&
                string.Equals(e.Uri, permittedUrl, StringComparison.OrdinalIgnoreCase);
            // Permit anchors in the currently displayed dictionary entry.
            if (!permitted && permittedUrl.StartsWith(WebResourceHandler.Origin + "/entry?id=",
                    StringComparison.Ordinal) &&
                e.Uri.StartsWith(permittedUrl + "#", StringComparison.Ordinal))
                permitted = true;

            if (!permitted)
            {
                e.Cancel = true;
                return;
            }
            if (navigationBusy && string.Equals(e.Uri, permittedUrl,
                    StringComparison.OrdinalIgnoreCase))
                navigationId = e.NavigationId;
        }

        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
        }

        private void OnPermissionRequested(object sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            e.State = CoreWebView2PermissionState.Deny;
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            // Ignore frames, foreign origins and callbacks from replaced documents.
            if (disposed || (e.Source != permittedUrl && !e.Source.StartsWith(permittedUrl + "#", StringComparison.Ordinal)))
                return;
            try
            {
                using var message = JsonDocument.Parse(e.WebMessageAsJson);
                var root = message.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("value", out var value)) return;
                string action = type.GetString();
                if (action == "mdict-copy" || action == "mdict-copy-lookup")
                {
                    if (navigationBusy || value.ValueKind != JsonValueKind.String) return;
                    string text = value.GetString();
                    if (string.IsNullOrWhiteSpace(text)) return;
                    bool lookup = action == "mdict-copy-lookup";
                    if (lookup)
                    {
                        text = text.Trim();
                        if (text.Length > 512) return;
                        foreach (char character in text) if (char.IsControl(character)) return;
                    }
                    TextSelectionRequested?.Invoke(text, lookup);
                    return;
                }
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) ||
                    !double.IsFinite(number) || Math.Abs(number) > 1000000) return;
                switch (type.GetString())
                {
                    case "mdict-height" when number > 0:
                        contentHeight = number;
                        ContentHeightChanged?.Invoke(number * view.ZoomFactor);
                        break;
                    case "mdict-scroll": ScrollRequested?.Invoke(number * view.ZoomFactor); break;
                    case "mdict-anchor" when number >= 0: AnchorRequested?.Invoke(number * view.ZoomFactor); break;
                }
            }
            catch (JsonException) { }
        }

        private void OnZoomFactorChanged(object sender, EventArgs e)
        {
            if (!disposed && contentHeight > 0)
                ContentHeightChanged?.Invoke(contentHeight * view.ZoomFactor);
        }

        public async Task<int> ShowEntryAsync(string html)
        {
            if (html == null)
                throw new ArgumentNullException(nameof(html));
            // Keep Oxford's fragment and its relative CSS/JS/image references intact.
            // Inline onclick attributes require 'unsafe-inline' in script-src.
            // All non-local resource requests are blocked by WebResourceHandler.
            string page = "<!doctype html><html><head><meta charset=\"utf-8\">" +
                "<meta http-equiv=\"Content-Security-Policy\" content=\"" +
                "default-src 'none'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; font-src 'self' data:; " +
                "script-src 'self' 'unsafe-inline'; " +
                "connect-src 'none'; media-src 'self'; " +
                "frame-src 'none'; object-src 'none'; form-action 'none'; base-uri 'none'\">" +
                "<style>html,body{font-size:14px}</style></head><body>" + html + "</body></html>";

            string url = resources.PreparePage(page);
            return await NavigateAndInspectAsync(url, 60000, false);
        }

        // F9 does not require an opened dictionary or any MDD resources.
        // Use the same proven virtual-HTTPS navigation as a normal entry:
        // NavigateToString() is canceled by some WebView2 navigation sequences.
        public async Task<int> ShowDiagnosticAsync()
        {
            string page = "<!doctype html><html><head><meta charset=\"utf-8\"></head><body " +
                "style=\"padding:25px;font-family:Segoe UI;background:#fff;color:#202020\">" +
                "<h1 style=\"color:#116d44\">WebView2 显示测试</h1>" +
                "<p>如果看到这段文字，说明浏览器能显示普通 HTML。</p>" +
                "<p>在输入框内按回车可返回词条。</p></body></html>";
            string url = resources.PreparePage(page);
            return await NavigateAndInspectAsync(url, 15000);
        }

        public string GetResourceSummary()
        {
            return "CSS " + resources.CssSuccess + " | JS " + resources.JsSuccess +
                " | 图片 " + resources.ImageSuccess + " | 资源成功 " +
                resources.ResourceSuccess + " / 失败 " + resources.ResourceFailures;
        }

        private async Task<int> NavigateAndInspectAsync(string url, int timeoutMilliseconds, bool requireText = true)
        {
            if (disposed) throw new ObjectDisposedException(nameof(WebViewController));
            if (!initialized)
                throw new InvalidOperationException("WebView2 尚未初始化。");
            if (navigationBusy)
                throw new InvalidOperationException("上一个页面仍在加载中。");

            TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs> completed =
                new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            pendingNavigation = completed;
            // Both Oxford entries and the F9 self-test use the same HTTPS route.
            // Only accept the completion of our own navigation, not an older one.
            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler =
                (sender, e) =>
                {
                    if (navigationId != 0 && navigationId == e.NavigationId)
                        completed.TrySetResult(e);
                };

            permittedUrl = url;
            navigationBusy = true;
            navigationId = 0;
            view.CoreWebView2.NavigationCompleted += handler;
            try
            {
                view.CoreWebView2.Navigate(url);

                Task finished = await Task.WhenAny(completed.Task, Task.Delay(timeoutMilliseconds));
                if (finished != completed.Task)
                    throw new InvalidOperationException("WebView2 页面加载超时（" +
                        (timeoutMilliseconds / 1000) + " 秒）。");

                CoreWebView2NavigationCompletedEventArgs result = await completed.Task;
                if (!result.IsSuccess)
                    throw new InvalidOperationException("WebView2 页面加载失败：" + result.WebErrorStatus);

                string value = await view.CoreWebView2.ExecuteScriptAsync(
                    "document.body ? document.body.innerText.trim().length : -1");
                int length;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out length) ||
                    length < 0 || (requireText && length == 0))
                    throw new InvalidOperationException("页面 DOM 没有可见文字：" + value);
                return length;
            }
            finally
            {
                navigationBusy = false;
                pendingNavigation = null;
                if (!disposed) view.CoreWebView2.NavigationCompleted -= handler;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            pendingNavigation?.TrySetCanceled();
            view.ZoomFactorChanged -= OnZoomFactorChanged;
            zoomProperty?.RemoveValueChanged(view, OnZoomFactorChanged);
            resources?.Dispose();
            var core = view.CoreWebView2;
            if (core != null)
            {
                core.NavigationStarting -= OnNavigationStarting;
                core.NewWindowRequested -= OnNewWindowRequested;
                core.PermissionRequested -= OnPermissionRequested;
                core.WebMessageReceived -= OnWebMessageReceived;
            }
            EntryLookupRequested = null;
            TextSelectionRequested = null;
            ContentHeightChanged = null;
            ScrollRequested = null;
            AnchorRequested = null;
        }
    }
}
