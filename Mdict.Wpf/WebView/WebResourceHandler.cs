using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Mdict.Application;

namespace Mdict.Wpf.WebView
{
    // Translate requests on our private HTTPS origin into HTML or MDD bytes.
    // This class knows about WebView2, never about MDX/MDD internals.
    public class WebResourceHandler : IDisposable
    {
        public const string Origin = "https://mdict.invalid";

        private readonly DictionaryService dictionaryService;
        private readonly CoreWebView2 browser;
        private long currentPageId;
        private string currentPage = "";
        private bool disposed;

        public int CssSuccess { get; private set; }
        public int JsSuccess { get; private set; }
        public int ImageSuccess { get; private set; }
        public int ResourceSuccess { get; private set; }
        public int ResourceFailures { get; private set; }
        public string LastError { get; private set; } = "";

        public WebResourceHandler(CoreWebView2 webView, DictionaryService service)
        {
            browser = webView;
            dictionaryService = service;
        }

        // New page id prevents an old navigation receiving a newer word's HTML.
        public string PreparePage(string html)
        {
            currentPageId++;
            currentPage = html;
            CssSuccess = 0;
            JsSuccess = 0;
            ImageSuccess = 0;
            ResourceSuccess = 0;
            ResourceFailures = 0;
            LastError = "";
            return Origin + "/entry?id=" + currentPageId;
        }

        public void Register()
        {
            // Catch both our private origin and any unexpected external URL.
            // Do not allow ordinary network fallback for an Oxford document.
            browser.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            browser.WebResourceRequested += OnWebResourceRequested;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            browser.WebResourceRequested -= OnWebResourceRequested;
        }

        private async void OnWebResourceRequested(object sender,
            CoreWebView2WebResourceRequestedEventArgs e)
        {
            string url = e.Request.Uri;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, "mdict.invalid", StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort ||
                !string.Equals(e.Request.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                e.Response = MakeTextResponse(403, "Forbidden", "Request blocked");
                return;
            }

            if (uri.AbsolutePath == "/entry")
            {
                // Never serve the next word to an old, late navigation request.
                string expected = "/entry?id=" + currentPageId;
                if (!string.Equals(uri.PathAndQuery, expected, StringComparison.Ordinal))
                    e.Response = MakeTextResponse(410, "Gone", "Page replaced");
                else
                    e.Response = MakeResponse(Encoding.UTF8.GetBytes(currentPage),
                        "text/html; charset=utf-8", 200, "OK");
                return;
            }

            string key;
            if (!TryResourceKey(uri, out key))
            {
                e.Response = MakeTextResponse(400, "Bad Request", "Invalid resource path");
                return;
            }

            string mime = GetContentType(key);
            if (mime == "application/octet-stream")
            {
                e.Response = MakeTextResponse(404, "Not Found", "Unsupported resource");
                return;
            }

            // Capture headers before awaiting, while still on WebView2's event thread.
            string rangeHeader = null;
            if (mime == "audio/mpeg" && e.Request.Headers.Contains("Range"))
                rangeHeader = e.Request.Headers.GetHeader("Range");

            // Resource opening (especially a new MDD volume) can take time.
            // A deferral avoids blocking WebView2's UI-thread message loop.
            using (e.GetDeferral())
            {
                try
                {
                    byte[] content = await Task.Run(() => dictionaryService.GetResource(key));
                    if (disposed) return;
                    if (content == null)
                    {
                        ResourceFailures++;
                        LastError = key + " (not found)";
                        Debug.WriteLine("[MdictViewer MDD 404] " + key);
                        e.Response = MakeTextResponse(404, "Not Found", "Resource not found");
                    }
                    else
                    {
                        ResourceSuccess++;
                        if (mime == "text/css") CssSuccess++;
                        if (mime == "application/javascript") JsSuccess++;
                        if (mime.StartsWith("image/", StringComparison.Ordinal)) ImageSuccess++;
                        string responseType = mime;
                        if (mime == "text/css" || mime == "application/javascript")
                            responseType += "; charset=utf-8";
                        if (mime == "audio/mpeg")
                            e.Response = MakeAudioResponse(content, rangeHeader);
                        else
                            e.Response = MakeResponse(content, responseType, 200, "OK");
                    }
                }
                catch (Exception ex)
                {
                    if (disposed) return;
                    ResourceFailures++;
                    LastError = key + " (" + ex.Message + ")";
                    Debug.WriteLine("[MdictViewer MDD error] " + LastError);
                    e.Response = MakeTextResponse(500, "Internal Server Error", "Resource loading failed");
                }
            }
        }

        // Chromium may request a byte range for HTML Audio even for a small MP3.
        // Support a single byte range; multi-range requests are not needed here.
        private CoreWebView2WebResourceResponse MakeAudioResponse(byte[] data, string rangeHeader)
        {
            string total = data.Length.ToString(CultureInfo.InvariantCulture);
            string commonHeaders = "Accept-Ranges: bytes\r\n";

            if (rangeHeader == null)
                return MakeResponse(data, "audio/mpeg", 200, "OK", commonHeaders);

            int start;
            int end;
            if (!TryParseSingleRange(rangeHeader, data.Length, out start, out end))
            {
                return MakeResponse(Array.Empty<byte>(), "audio/mpeg", 416,
                    "Range Not Satisfiable",
                    commonHeaders + "Content-Range: bytes */" + total + "\r\n");
            }

            int count = end - start + 1;
            byte[] segment = new byte[count];
            Buffer.BlockCopy(data, start, segment, 0, count);
            string range = "Content-Range: bytes " +
                start.ToString(CultureInfo.InvariantCulture) + "-" +
                end.ToString(CultureInfo.InvariantCulture) + "/" + total + "\r\n";
            return MakeResponse(segment, "audio/mpeg", 206, "Partial Content",
                commonHeaders + range);
        }

        // Accept bytes=N-M, bytes=N-, bytes=-N. Reject multi-ranges and
        // unsupported forms rather than producing an incorrect 206 response.
        private static bool TryParseSingleRange(string header, int total,
            out int start, out int end)
        {
            start = 0;
            end = 0;
            if (total == 0 || header == null ||
                !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                return false;

            string spec = header.Substring(6).Trim();
            if (spec.Contains(','))
                return false;
            int dash = spec.IndexOf('-');
            if (dash < 0 || spec.IndexOf('-', dash + 1) >= 0)
                return false;

            string left = spec.Substring(0, dash).Trim();
            string right = spec.Substring(dash + 1).Trim();
            long first;
            long last;
            if (left.Length == 0)
            {
                long suffixLength;
                if (!long.TryParse(right, NumberStyles.None,
                        CultureInfo.InvariantCulture, out suffixLength) || suffixLength <= 0)
                    return false;
                first = Math.Max(0L, (long)total - suffixLength);
                last = total - 1;
            }
            else
            {
                if (!long.TryParse(left, NumberStyles.None,
                        CultureInfo.InvariantCulture, out first) || first >= total)
                    return false;
                if (right.Length == 0)
                    last = total - 1;
                else if (!long.TryParse(right, NumberStyles.None,
                        CultureInfo.InvariantCulture, out last) || last < first)
                    return false;
                last = Math.Min(last, total - 1L);
            }

            start = (int)first;
            end = (int)last;
            return true;
        }

        private CoreWebView2WebResourceResponse MakeTextResponse(
            int status, string reason, string text)
        {
            return MakeResponse(Encoding.UTF8.GetBytes(text),
                "text/plain; charset=utf-8", status, reason);
        }

        private CoreWebView2WebResourceResponse MakeResponse(
            byte[] bytes, string type, int status, string reason, string extraHeaders = "")
        {
            // Keep stream alive after this method returns: WebView2 consumes it later.
            MemoryStream stream = new MemoryStream(bytes, false);
            return browser.Environment.CreateWebResourceResponse(stream, status, reason,
                "Content-Type: " + type + "\r\n" +
                "Content-Length: " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "Cache-Control: no-store\r\n" +
                "X-Content-Type-Options: nosniff\r\n" + extraHeaders);
        }

        // A canonical MDD key has one leading backslash. Browser relative URLs
        // are resolved by the browser against /entry?id=<n> before arriving here.
        private static bool TryResourceKey(Uri uri, out string key)
        {
            key = "";
            string path;
            try
            {
                path = Uri.UnescapeDataString(uri.AbsolutePath);
            }
            catch (UriFormatException)
            {
                return false;
            }
            if (!path.StartsWith("/", StringComparison.Ordinal) || path.Length < 2 ||
                path.Contains('\\') || path.Contains('\0') || path.Contains(':') ||
                path.Contains('?') || path.Contains('#'))
                return false;

            string[] segments = path.Substring(1).Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                    return false;
                // Prevent a second pass of %-decoding from changing the path.
                if (segment.IndexOf('%') >= 0)
                    return false;
            }
            key = "\\" + string.Join("\\", segments);
            return true;
        }

        private static string GetContentType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".css": return "text/css";
                case ".js": return "application/javascript";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".svg": return "image/svg+xml";
                case ".webp": return "image/webp";
                case ".bmp": return "image/bmp";
                case ".ico": return "image/x-icon";
                case ".woff": return "font/woff";
                case ".woff2": return "font/woff2";
                case ".ttf": return "font/ttf";
                case ".otf": return "font/otf";
                case ".eot": return "application/vnd.ms-fontobject";
                case ".mp3": return "audio/mpeg";
                default: return "application/octet-stream";
            }
        }
    }
}
