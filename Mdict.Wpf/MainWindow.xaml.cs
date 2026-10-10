using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Mdict.Application;
using Mdict.Wpf.WebView;

namespace Mdict.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly DictionaryService dictionaryService = new DictionaryService();
        private WebViewController webViewController;
        private bool busy;
        private bool browserReady;

        // Navigation history is UI state. The browser's internal /entry and F9
        // navigations must never appear as dictionary history entries.
        private readonly List<string> history = new List<string>();
        private int historyIndex = -1;
        private string displayedWord = "";
        private const int MaxHistory = 50;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            webViewController = new WebViewController(DictionaryWebView, dictionaryService);
            webViewController.EntryLookupRequested += OnEntryLookupRequested;
            try
            {
                await webViewController.InitializeAsync();
                browserReady = true;
                StatusText.Text = "就绪。请选择 Oxford MDX 文件。";
                string[] args = Environment.GetCommandLineArgs();
                if (args.Length > 1 && File.Exists(args[1]))
                    await OpenDictionaryAsync(args[1]);
            }
            catch (Exception ex)
            {
                StatusText.Text = "WebView2 初始化失败：" + ex.Message;
                MessageBox.Show(this, ex.Message, "WebView2 初始化失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (!browserReady || busy)
                return;

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "选择牛津词典 MDX";
            dialog.Filter = "MDX 词典 (*.mdx)|*.mdx";
            dialog.CheckFileExists = true;
            if (dialog.ShowDialog(this) == true)
                await OpenDictionaryAsync(dialog.FileName);
        }

        private async Task OpenDictionaryAsync(string path)
        {
            SetBusy(true);
            StatusText.Text = "正在打开词典…";
            try
            {
                await Task.Run(() => dictionaryService.Open(path));
                // A newly opened dictionary starts a new browsing session.
                history.Clear();
                historyIndex = -1;
                displayedWord = "";
                DictionaryText.Text = dictionaryService.DictionaryName +
                    "  （MDD 分卷：" + dictionaryService.MddCount + "）";
                StatusText.Text = "词典已打开。请输入单词。";
                WordText.Text = "apple";
                await LookupAsync("apple", false);
            }
            catch (Exception ex)
            {
                StatusText.Text = "打开失败：" + ex.Message;
                MessageBox.Show(this, ex.Message, "打开词典失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await LookupAsync(WordText.Text, true);
        }

        private async void WordText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await LookupAsync(WordText.Text, true);
            }
        }

        // Raised after the bridge's /lookup navigation has been canceled.
        // Do not perform a new WebView2 navigation within NavigationStarting.
        private async void OnEntryLookupRequested(string key)
        {
            if (!browserReady || !dictionaryService.IsOpen || busy)
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

        // requestedHistoryIndex >= 0 means back navigation; it must not create
        // a new entry. Normal searches and link clicks share this same path.
        private async Task LookupAsync(string input, bool manageBusy, int requestedHistoryIndex = -1)
        {
            if (!dictionaryService.IsOpen || !browserReady)
                return;
            if (manageBusy && busy)
                return;
            string word = input == null ? "" : input.Trim();
            if (word.Length == 0)
                return;

            if (manageBusy)
                SetBusy(true);
            StatusText.Text = "正在查询 " + word + "…";
            try
            {
                string html = await Task.Run(() => dictionaryService.Lookup(word));
                if (html == null)
                {
                    // Keep the last successful page and history unchanged.
                    if (displayedWord.Length > 0)
                        WordText.Text = displayedWord;
                    StatusText.Text = "未找到词条：" + word;
                    return;
                }

                int visibleCharacters = await webViewController.ShowEntryAsync(html);
                displayedWord = word;
                WordText.Text = word;
                CommitHistory(word, requestedHistoryIndex);

                // Diagnostics are secondary: a failure here must not undo a
                // successfully displayed page or its history entry.
                string diagnostics = "";
                try
                {
                    diagnostics = await webViewController.GetDomResourceStateAsync() + "；";
                }
                catch (Exception)
                {
                    diagnostics = "DOM 资源统计不可用；";
                }
                StatusText.Text = "已加载：" + word + "；DOM " +
                    visibleCharacters + " 字符；" + diagnostics +
                    webViewController.GetResourceSummary();
                StatusText.ToolTip = webViewController.LastResourceError;
            }
            catch (Exception ex)
            {
                if (displayedWord.Length > 0)
                    WordText.Text = displayedWord;
                StatusText.Text = "查询失败：" + ex.Message;
                MessageBox.Show(this, ex.Message, "查询失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (manageBusy)
                    SetBusy(false);
            }
        }

        private void CommitHistory(string word, int requestedHistoryIndex)
        {
            if (requestedHistoryIndex >= 0)
            {
                historyIndex = requestedHistoryIndex;
                UpdateBackButton();
                return;
            }
            if (historyIndex >= 0 &&
                string.Equals(history[historyIndex], word, StringComparison.OrdinalIgnoreCase))
            {
                UpdateBackButton();
                return;
            }

            // If the user went back and then searches again, discard the old
            // forward branch; future versions can add forward navigation.
            if (historyIndex + 1 < history.Count)
                history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
            history.Add(word);
            if (history.Count > MaxHistory)
                history.RemoveAt(0);
            historyIndex = history.Count - 1;
            UpdateBackButton();
        }

        // F9 tests the browser; it is not a dictionary entry or history item.
        private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.F9 || !browserReady || busy)
                return;
            e.Handled = true;
            SetBusy(true);
            try
            {
                int length = await webViewController.ShowDiagnosticAsync();
                StatusText.Text = "WebView2 自测已加载；DOM 文本 " + length +
                    " 字符。点击查询返回词条。";
            }
            catch (Exception ex)
            {
                StatusText.Text = "WebView2 自测失败：" + ex.Message;
                MessageBox.Show(this, ex.Message, "WebView2 自测失败",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void UpdateBackButton()
        {
            BackButton.IsEnabled = !busy && browserReady &&
                dictionaryService.IsOpen && historyIndex > 0;
        }

        private void SetBusy(bool value)
        {
            busy = value;
            OpenButton.IsEnabled = !value && browserReady;
            SearchButton.IsEnabled = !value && browserReady && dictionaryService.IsOpen;
            WordText.IsEnabled = !value;
            UpdateBackButton();
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            dictionaryService.Close();
        }
    }
}
