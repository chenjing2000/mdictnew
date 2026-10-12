using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Interop;

namespace Mdict.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly List<DictionaryResultView> sections = new List<DictionaryResultView>();
        private readonly string settingsPath;
        private List<string> configuredPaths = new List<string>();
        private DictionaryResultView diagnosticView;
        private bool closed;
        private int configurationVersion;
        private bool busy;
        private bool browserReady;

        // Navigation history is UI state. The browser's internal /entry and F9
        // navigations must never appear as dictionary history entries.
        private readonly List<string> history = new List<string>();
        private int historyIndex = -1;
        private string displayedWord = "";
        private const int MaxHistory = 50;

        // Suggestions are transient UI state, not dictionary navigation/history.
        private const int MaxSuggestions = 12;
        private const int PrefixFetchLimit = 48;
        private readonly DispatcherTimer suggestionTimer = new DispatcherTimer();
        private readonly DispatcherTimer statusTimer = new DispatcherTimer();
        private bool controlsReady;
        private bool updatingWordText;
        private bool suggestionSearchRunning;
        private int suggestionVersion;
        private int pendingSuggestionVersion;
        private string pendingSuggestionPrefix;
        private string displayedSuggestionPrefix;

        public MainWindow() : this(DictionarySettings.DefaultPath) { }

        public MainWindow(string configurationPath)
        {
            settingsPath = configurationPath;
            InitializeComponent();
            statusTimer.Interval = TimeSpan.FromSeconds(6);
            statusTimer.Tick += (_, _) => ShowStatus("");
            ShowStatus("正在初始化…");
            suggestionTimer.Interval = TimeSpan.FromMilliseconds(150);
            suggestionTimer.Tick += SuggestionTimer_Tick;
            controlsReady = true;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = (HwndSource)PresentationSource.FromVisual(this);
            HwndSourceHook windowMessages = (IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                // Keep the window icon without opening its left-click system menu.
                if (message == 0x00A1 && wParam.ToInt64() == 3) handled = true;
                // WM_ENTERSIZEMOVE precedes moving/resizing, even with input focus intact.
                if (message == 0x0231) CloseSuggestions();
                return IntPtr.Zero;
            };
            source.AddHook(windowMessages);
            Closed += (_, _) => { if (!source.IsDisposed) source.RemoveHook(windowMessages); };
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                browserReady = true;
                string[] args = Environment.GetCommandLineArgs();
                if (args.Length > 1 && File.Exists(args[1]))
                    configuredPaths = new List<string> { args[1] };
                else
                    configuredPaths = DictionarySettings.Load(settingsPath);
                await ApplyDictionaryPathsAsync(configuredPaths);
            }
            catch (Exception ex)
            {
                if (!closed) ShowStatus("设置加载失败：" + ex.Message);
            }
            if (!closed)
            {
                try
                {
                    string runtimeError = WebView.WebViewController.GetRuntimeError();
                    if (runtimeError != null)
                    {
                        ShowStatus("请安装 Microsoft Edge WebView2 Runtime 后重试。");
                        StatusText.ToolTip = runtimeError;
                        RuntimeHelp.Visibility = Visibility.Visible;
                    }
                }
                catch (Exception ex)
                {
                    ShowStatus("浏览器运行环境检测失败：" + ex.Message);
                    StatusText.ToolTip = StatusText.Text;
                }
            }
        }

        private void RuntimeDownload_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            e.Handled = true;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    WebView.WebViewController.RuntimeDownloadUrl) { UseShellExecute = true });
            }
            catch (Exception ex) { ShowStatus("无法打开下载页面：" + ex.Message); }
        }

        private async void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!browserReady || busy)
                return;
            CloseSuggestions();
            try { DictionarySettings.Initialize(settingsPath); }
            catch (Exception ex)
            {
                ShowStatus("设置初始化失败：" + ex.Message);
                StatusText.ToolTip = StatusText.Text;
                return;
            }
            var dialog = new SettingsWindow(configuredPaths, settingsPath) { Owner = this };
            dialog.ShowDialog();
            if (!closed && dialog.HasChanges)
            {
                try
                {
                    var paths = dialog.ResultPaths.ToList();
                    await ApplyDictionaryPathsAsync(paths);
                }
                catch (Exception ex)
                {
                    if (!closed) ShowStatus("词典设置应用失败：" + ex.Message);
                }
            }
            if (!closed) WordText.Focus();
        }

        private DictionaryResultView CreateSection(string path)
        {
            var section = new DictionaryResultView(path);
            section.Controller.EntryLookupRequested += key =>
            {
                if (!closed && sections.Contains(section)) OnEntryLookupRequested(key);
            };
            section.Controller.TextSelectionRequested += (text, lookup) =>
            {
                if (!closed && sections.Contains(section)) OnTextSelectionRequested(text, lookup);
            };
            section.Controller.ScrollRequested += delta =>
            {
                if (!closed) ResultsScroll.ScrollToVerticalOffset(ResultsScroll.VerticalOffset + delta);
            };
            section.Controller.AnchorRequested += top =>
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (closed || !sections.Contains(section)) return;
                    ResultsPanel.UpdateLayout();
                    double origin = section.Browser.TranslatePoint(new Point(0, 0), ResultsPanel).Y;
                    ResultsScroll.ScrollToVerticalOffset(origin + top);
                }));
            };
            section.Browser.GotKeyboardFocus += DictionaryWebView_GotKeyboardFocus;
            ResultsPanel.Children.Add(section);
            return section;
        }

        private void ShowStatus(string text)
        {
            statusTimer.Stop();
            StatusText.Text = text;
            StatusText.ToolTip = null;
            RuntimeHelp.Visibility = Visibility.Collapsed;
            StatusPanel.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (text.Length > 0) statusTimer.Start();
        }

        private async void OnTextSelectionRequested(string text, bool lookup)
        {
            string copyError = null;
            try { Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.ExternalException ex) { copyError = ex.Message; }
            if (!lookup || busy)
            {
                ShowStatus(copyError == null ? "已复制所选文字。" : "复制失败：" + copyError);
                return;
            }
            int version = configurationVersion;
            await LookupAsync(text, true);
            if (closed || version != configurationVersion) return;
            string result = StatusText.Text;
            if (copyError != null) ShowStatus("复制失败：" + copyError + "；" + result);
            else ShowStatus(result.StartsWith("已加载：", StringComparison.Ordinal)
                ? result.Replace("已加载：", "已复制并查询：") : "已复制；" + result);
        }

        private async Task ApplyDictionaryPathsAsync(IEnumerable<string> paths)
        {
            SetBusy(true);
            int version = ++configurationVersion;
            string word = displayedWord;
            configuredPaths = paths.ToList();
            foreach (var section in sections) section.Dispose();
            sections.Clear();
            diagnosticView?.Dispose();
            diagnosticView = null;
            ResultsPanel.Children.Clear();
            EmptyText.Visibility = configuredPaths.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ResultsPanel.Children.Add(EmptyText);
            history.Clear();
            historyIndex = -1;
            displayedWord = "";
            ShowStatus("正在打开词典…");
            var openErrors = new List<string>();
            try
            {
                foreach (string path in configuredPaths)
                {
                    if (closed || version != configurationVersion) return;
                    var section = CreateSection(path);
                    sections.Add(section);
                    try
                    {
                        await section.OpenAsync();
                        if (closed || version != configurationVersion) return;
                        section.IsAvailable = true;
                    }
                    catch (Exception ex)
                    {
                        if (closed || version != configurationVersion) return;
                        openErrors.Add(Path.GetFileNameWithoutExtension(path) + "：" + ex.Message);
                    }
                }
                if (closed) return;
                ShowStatus(configuredPaths.Count == 0 ? "尚未设置词典。" :
                    openErrors.Count > 0 ? "词典打开失败：" + string.Join("；", openErrors) : "词典已打开。");
                if (word.Length > 0 && configuredPaths.Count > 0) await LookupAsync(word, false);
            }
            finally
            {
                if (!closed && version == configurationVersion) SetBusy(false);
            }
        }

        private async void WordText_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down || e.Key == Key.Up)
            {
                if (!HasCurrentSuggestions())
                    return;
                e.Handled = true;
                int count = SuggestionList.Items.Count;
                int index = SuggestionList.SelectedIndex;
                if (e.Key == Key.Down)
                    index = (index + 1) % count;
                else
                    index = index < 0 ? count - 1 : (index - 1 + count) % count;
                SuggestionList.SelectedIndex = index;
                SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
            }
            else if (e.Key == Key.Escape)
            {
                if (SuggestionPopup.IsOpen || suggestionTimer.IsEnabled ||
                    pendingSuggestionPrefix != null || suggestionSearchRunning)
                {
                    e.Handled = true;
                    CloseSuggestions();
                }
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                string word = HasCurrentSuggestions() && SuggestionList.SelectedItem is string selected
                    ? selected : WordText.Text;
                await LookupAsync(word, true, -1, true);
            }
            else if (e.Key == Key.Tab)
            {
                // Cancel even when the mouse is still over the Popup.
                // Leave Handled false so normal keyboard traversal continues.
                CloseSuggestions();
            }
        }

        private void WordText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!controlsReady || updatingWordText)
                return;

            // Don't tear down/reopen the popup on every keystroke.
            // The previous entries remain visible during debounce, but are
            // never selectable until the results match the current prefix.
            suggestionVersion++;
            suggestionTimer.Stop();
            pendingSuggestionPrefix = null;
            displayedSuggestionPrefix = null;
            SuggestionList.SelectedIndex = -1;
            if (!CanSuggest() || string.IsNullOrWhiteSpace(WordText.Text))
            {
                CloseSuggestions();
                return;
            }
            suggestionTimer.Start();
        }

        private bool HasCurrentSuggestions()
        {
            return SuggestionPopup.IsOpen && SuggestionList.Items.Count > 0 &&
                string.Equals(displayedSuggestionPrefix, WordText.Text.Trim(),
                    StringComparison.Ordinal);
        }

        private bool CanSuggest()
        {
            return !closed && browserReady && !busy && sections.Any(s => s.IsAvailable) &&
                WordText.IsEnabled;
        }

        private void SuggestionTimer_Tick(object sender, EventArgs e)
        {
            suggestionTimer.Stop();
            if (!CanSuggest() || !WordText.IsKeyboardFocused)
                return;

            string prefix = WordText.Text.Trim();
            if (prefix.Length == 0)
                return;
            pendingSuggestionPrefix = prefix;
            pendingSuggestionVersion = suggestionVersion;
            _ = ProcessSuggestionQueueAsync();
        }

        // Serialize candidate requests: the existing service uses a shared
        // reader lock for searches and WebView2's CSS/image/audio resources.
        // A new input replaces a pending request rather than starting a queue.
        private async Task ProcessSuggestionQueueAsync()
        {
            if (suggestionSearchRunning)
                return;

            suggestionSearchRunning = true;
            try
            {
                while (pendingSuggestionPrefix != null)
                {
                    string prefix = pendingSuggestionPrefix;
                    int version = pendingSuggestionVersion;
                    pendingSuggestionPrefix = null;

                    List<string> keys;
                    var errors = new List<string>();
                    try
                    {
                        var readers = sections.Where(s => s.IsAvailable).ToArray();
                        keys = await Task.Run(() =>
                        {
                            var merged = new List<string>();
                            foreach (var section in readers)
                            {
                                try { merged.AddRange(section.Service.SearchPrefix(prefix, PrefixFetchLimit)); }
                                catch (Exception ex)
                                {
                                    errors.Add(Path.GetFileNameWithoutExtension(section.DictionaryPath) + "：" + ex.Message);
                                }
                            }
                            return merged;
                        });
                    }
                    catch (Exception ex)
                    {
                        if (version == suggestionVersion && CanSuggest())
                            ShowStatus("候选词查询失败：" + ex.Message);
                        continue;
                    }

                    if (version != suggestionVersion || !CanSuggest() ||
                        !WordText.IsKeyboardFocused ||
                        !string.Equals(WordText.Text.Trim(), prefix, StringComparison.Ordinal))
                        continue;

                    if (errors.Count > 0) ShowStatus("部分词典候选词查询失败：" + string.Join("；", errors));
                    HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    List<string> suggestions = new List<string>();
                    foreach (string key in keys)
                    {
                        if (!string.IsNullOrEmpty(key) && seen.Add(key))
                        {
                            suggestions.Add(key);
                            if (suggestions.Count == MaxSuggestions)
                                break;
                        }
                    }
                    ShowSuggestions(suggestions, prefix);
                }
            }
            finally
            {
                suggestionSearchRunning = false;
            }
        }

        private void ShowSuggestions(List<string> words, string prefix)
        {
            // Keep an existing Popup open while updating its items. Closing
            // and recreating it on every input can interfere with WPF focus.
            if (words.Count == 0)
            {
                CloseSuggestions();
                return;
            }
            SuggestionBorder.Width = WordText.ActualWidth;
            SuggestionList.Items.Clear();
            foreach (string word in words)
                SuggestionList.Items.Add(word);
            SuggestionList.SelectedIndex = -1;
            displayedSuggestionPrefix = prefix;
            if (!SuggestionPopup.IsOpen)
            {
                SuggestionPopup.IsOpen = true;
                // Popup content must not take keyboard input away from TextBox.
                if (!WordText.IsKeyboardFocused)
                    WordText.Focus();
            }
        }

        private void CloseSuggestions()
        {
            // Invalidate already running background results as well as timers.
            suggestionVersion++;
            suggestionTimer.Stop();
            pendingSuggestionPrefix = null;
            displayedSuggestionPrefix = null;
            if (!controlsReady)
                return;
            SuggestionPopup.IsOpen = false;
            SuggestionList.SelectedIndex = -1;
            SuggestionList.Items.Clear();
        }

        private void SetWordText(string word)
        {
            updatingWordText = true;
            try
            {
                WordText.Text = word;
            }
            finally
            {
                updatingWordText = false;
            }
        }

        private async void SuggestionList_PreviewMouseLeftButtonDown(object sender,
            MouseButtonEventArgs e)
        {
            ListBoxItem item = ItemsControl.ContainerFromElement(
                SuggestionList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item == null || !HasCurrentSuggestions())
                return;
            string word = item.Content as string;
            if (string.IsNullOrEmpty(word))
                return;
            // Commit on mouse-down, before Popup focus/deactivation can
            // dismiss the item and swallow its mouse-up event.
            e.Handled = true;
            await LookupAsync(word, true, -1, true);
        }

        private void WordText_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (controlsReady)
                SuggestionBorder.Width = WordText.ActualWidth;
        }

        private void WordText_LostKeyboardFocus(object sender,
            KeyboardFocusChangedEventArgs e)
        {
            // Do not close in the mouse-down phase of a popup item click.
            // Defer until the candidate's preview mouse-down has been processed.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (controlsReady && !WordText.IsKeyboardFocused)
                    CloseSuggestions();
            }));
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Popup has a separate HWND, but its preview events still route
            // through the logical Window ancestor before reaching the item.
            if (e.OriginalSource is DependencyObject source &&
                (WordText.IsAncestorOf(source) || SuggestionBorder.IsAncestorOf(source)))
                return;
            CloseSuggestions();
        }

        private void DictionaryWebView_GotKeyboardFocus(object sender,
            KeyboardFocusChangedEventArgs e)
        {
            CloseSuggestions();
        }

        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            // The Popup uses its own native window. A click inside it must
            // not dismiss suggestions before the item MouseDown handler runs.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!SuggestionPopup.IsOpen || !SuggestionList.IsMouseOver)
                    CloseSuggestions();
            }));
        }

        // Raised after the bridge's /lookup navigation has been canceled.
        // Do not perform a new WebView2 navigation within NavigationStarting.
        private async void OnEntryLookupRequested(string key)
        {
            if (closed || !browserReady || busy)
                return;
            await LookupAsync(key, true);
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (busy || historyIndex <= 0)
                return;
            int targetIndex = historyIndex - 1;
            await LookupAsync(history[targetIndex], true, targetIndex);
        }

        private async void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (busy || historyIndex < 0 || historyIndex + 1 >= history.Count) return;
            int targetIndex = historyIndex + 1;
            await LookupAsync(history[targetIndex], true, targetIndex);
        }

        // requestedHistoryIndex >= 0 means history navigation; it must not create
        // a new entry. Normal searches and link clicks share this same path.
        private async Task LookupAsync(string input, bool manageBusy,
            int requestedHistoryIndex = -1, bool preserveInputOnFailure = false)
        {
            if (closed || !browserReady)
                return;
            if (manageBusy && busy)
                return;
            string word = input == null ? "" : input.Trim();
            string previousWord = displayedWord;
            foreach (var section in sections) section.Visibility = Visibility.Collapsed;
            if (diagnosticView != null) diagnosticView.Visibility = Visibility.Collapsed;
            displayedWord = "";
            StatusText.ToolTip = null;
            EmptyText.Visibility = Visibility.Collapsed;
            ResultsScroll.ScrollToTop();
            CloseSuggestions();
            if (word.Length == 0)
            {
                ShowStatus("请输入查询词。");
                return;
            }

            if (manageBusy)
                SetBusy(true);
            ShowStatus("正在查询 " + word + "…");
            int version = configurationVersion;
            try
            {
                var readers = sections.ToArray();
                var results = await Task.Run(() =>
                {
                    var entries = new List<(DictionaryResultView Section, string Html, string Error)>();
                    foreach (var section in readers)
                    {
                        string html = null;
                        string error = null;
                        if (section.IsAvailable)
                        {
                            try { html = section.Service.Lookup(word); }
                            catch (Exception ex) { error = ex.Message; }
                        }
                        else error = "词典未打开";
                        entries.Add((section, html, error));
                    }
                    return entries;
                });
                if (closed || version != configurationVersion) return;
                int matched = 0;
                int loaded = 0;
                var displayErrors = new List<string>();
                foreach (var result in results)
                {
                    if (result.Error != null)
                        displayErrors.Add(Path.GetFileNameWithoutExtension(result.Section.DictionaryPath) + "：" + result.Error);
                    if (!result.Section.IsAvailable || string.IsNullOrWhiteSpace(result.Html)) continue;
                    matched++;
                    try
                    {
                        result.Section.PrepareResult();
                        await result.Section.InitializeBrowserAsync();
                        if (closed || version != configurationVersion) return;
                        await result.Section.Controller.ShowEntryAsync(result.Html);
                        if (closed || version != configurationVersion) return;
                        result.Section.ShowBrowser();
                        loaded++;
                    }
                    catch (Exception ex)
                    {
                        result.Section.Visibility = Visibility.Collapsed;
                        displayErrors.Add(Path.GetFileNameWithoutExtension(result.Section.DictionaryPath) + "：" + ex.Message);
                    }
                    if (closed || version != configurationVersion) return;
                }
                if (loaded == 0)
                {
                    ShowStatus((matched == 0 ? "未找到词条：" : "词条显示失败：") + word +
                        (displayErrors.Count > 0 ? "；" + string.Join("；", displayErrors) : ""));
                    return;
                }
                displayedWord = word;
                RuntimeHelp.Visibility = Visibility.Collapsed;
                SetWordText(word);
                CommitHistory(word, requestedHistoryIndex);
                ResultsScroll.ScrollToTop();
                ShowStatus("已加载：" + word + "；命中 " + loaded + " / " + sections.Count + " 个词典" +
                    (displayErrors.Count > 0 ? "；" + string.Join("；", displayErrors) : ""));
                StatusText.ToolTip = string.Join("\n", sections.Where(s => s.IsAvailable && s.Visibility == Visibility.Visible)
                    .Select(s => Path.GetFileNameWithoutExtension(s.DictionaryPath) + "：" + s.Controller.GetResourceSummary()));
            }
            catch (Exception ex)
            {
                if (closed || version != configurationVersion) return;
                ShowStatus("查询失败：" + ex.Message);
            }
            finally
            {
                if (!closed && version == configurationVersion && displayedWord.Length == 0 &&
                    !preserveInputOnFailure && previousWord.Length > 0)
                    SetWordText(previousWord);
                if (manageBusy && !closed && version == configurationVersion)
                    SetBusy(false);
            }
        }

        private void CommitHistory(string word, int requestedHistoryIndex)
        {
            if (requestedHistoryIndex >= 0)
            {
                historyIndex = requestedHistoryIndex;
                UpdateNavigationButtons();
                return;
            }
            if (historyIndex >= 0 &&
                string.Equals(history[historyIndex], word, StringComparison.OrdinalIgnoreCase))
            {
                UpdateNavigationButtons();
                return;
            }

            // If the user went back and then searches again, discard the old
            // forward branch.
            if (historyIndex + 1 < history.Count)
                history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
            history.Add(word);
            if (history.Count > MaxHistory)
                history.RemoveAt(0);
            historyIndex = history.Count - 1;
            UpdateNavigationButtons();
        }

        // F9 tests the browser; it is not a dictionary entry or history item.
        private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.F9 || !browserReady || busy || closed)
                return;
            e.Handled = true;
            SetBusy(true);
            DictionaryResultView target = null;
            try
            {
                target = sections.FirstOrDefault(s => s.IsAvailable);
                if (target == null)
                {
                    if (diagnosticView == null)
                    {
                        diagnosticView = CreateSection(null);
                        EmptyText.Visibility = Visibility.Collapsed;
                        try
                        {
                            diagnosticView.PrepareResult();
                            await diagnosticView.InitializeBrowserAsync();
                        }
                        catch
                        {
                            diagnosticView.Dispose();
                            ResultsPanel.Children.Remove(diagnosticView);
                            diagnosticView = null;
                            EmptyText.Visibility = configuredPaths.Count == 0
                                ? Visibility.Visible : Visibility.Collapsed;
                            throw;
                        }
                    }
                    target = diagnosticView;
                }
                target.PrepareResult();
                await target.InitializeBrowserAsync();
                int length = await target.Controller.ShowDiagnosticAsync();
                target.ShowBrowser();
                if (!closed) ShowStatus("WebView2 自测已加载；DOM 文本 " + length + " 字符。");
            }
            catch (Exception ex)
            {
                if (target != null) target.Visibility = Visibility.Collapsed;
                if (!closed) ShowStatus("WebView2 自测失败：" + ex.Message);
            }
            finally
            {
                if (!closed) SetBusy(false);
            }
        }

        private void UpdateNavigationButtons()
        {
            BackButton.IsEnabled = !busy && browserReady &&
                sections.Any(s => s.IsAvailable) && historyIndex > 0;
            ForwardButton.IsEnabled = !busy && browserReady && historyIndex >= 0 && historyIndex + 1 < history.Count;
        }

        private void SetBusy(bool value)
        {
            if (value)
                CloseSuggestions();
            busy = value;
            SettingsButton.IsEnabled = !value && browserReady;
            // Disabling a focused TextBox transfers keyboard focus to another control.
            WordText.IsReadOnly = value;
            UpdateNavigationButtons();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            closed = true;
            statusTimer.Stop();
            configurationVersion++;
            CloseSuggestions();
            foreach (var section in sections) section.Dispose();
            diagnosticView?.Dispose();
        }
    }
}
