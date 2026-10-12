using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mdict.Wpf;

internal static class Program
{
    private static MainWindow window;
    private static TextBox input;
    private static ListBox list;
    private static Popup popup;
    private static int failures;
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--local-resources") return LocalResourceTests.Run();
        Console.WriteLine("Runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        Console.WriteLine("WPF: " + typeof(System.Windows.Application).Assembly.Location);
        if (args.Length != 1 || !File.Exists(args[0]))
        {
            Console.Error.WriteLine("Usage: dotnet run --project dev-tests/Mdict.Wpf.Tests -- <Oxford.mdx>");
            return 2;
        }
        var app = new System.Windows.Application();
        app.DispatcherUnhandledException += (_, e) =>
        {
            Check(false, "unexpected dispatcher exception: " + e.Exception);
            e.Handled = true;
        };
        string configurationPath = Path.Combine(Path.GetTempPath(), "MdictViewer-tests-" + Guid.NewGuid() + ".json");
        File.WriteAllText(configurationPath, "{ broken configuration");
        window = new MainWindow(configurationPath);
        input = (TextBox)window.FindName("WordText");
        list = (ListBox)window.FindName("SuggestionList");
        popup = (Popup)window.FindName("SuggestionPopup");
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                await WaitUntil(() => Field<bool>("browserReady") && !Field<bool>("busy"), "initial dictionary open");
                Check(Field<List<DictionaryResultView>>("sections").Count == 1 &&
                    Field<List<DictionaryResultView>>("sections")[0].IsAvailable &&
                    File.ReadAllText(configurationPath) == "{ broken configuration",
                    "command-line dictionary bypasses corrupt saved configuration without rewriting it");
                Check(input.Text == "" && Field<string>("displayedWord") == "" &&
                    Field<List<string>>("history").Count == 0, "startup leaves input and lookup history empty");
                Check(Field<List<DictionaryResultView>>("sections").All(s => s.Visibility == Visibility.Collapsed && s.Browser.CoreWebView2 == null),
                    "startup hides every dictionary container without initializing browsers");
                var service = Field<List<DictionaryResultView>>("sections")[0].Service;
                var reader = service.GetType().GetField("dictionary", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service);
                Check(reader.GetType().GetField("mdxKeys", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(reader) == null,
                    "startup does not load the word index or perform an implicit lookup");
                Capture((FrameworkElement)window.Content, "startup-empty.png");
                Check(Math.Abs(window.MaxWidth - SystemParameters.WorkArea.Width) < 1 &&
                    Math.Abs(window.MaxHeight - SystemParameters.WorkArea.Height) < 1,
                    "main window maximum dimensions match the available screen without a fixed cap");
                TestWindowLimits(window, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height, "main window");
                Check(window.Icon != null, "main window has the drawn M icon");
                TestAppIcon();
                input.Clear();
                Press(Key.Enter);
                await Idle();
                Check(Field<string>("displayedWord") == "" && Field<List<string>>("history").Count == 0,
                    "Enter with empty input does not query or add history");
                Edit("apple");
                Press(Key.Enter);
                await Idle();
                Check(Field<string>("displayedWord") == "apple", "real Oxford opens apple");
                var restarted = new MainWindow(configurationPath) { Owner = window };
                try
                {
                    restarted.Show();
                    await WaitUntil(() => Field<bool>("browserReady", restarted) && !Field<bool>("busy", restarted), "reopened window");
                    Check(((TextBox)restarted.FindName("WordText")).Text == "" &&
                        Field<string>("displayedWord", restarted) == "" && Field<List<string>>("history", restarted).Count == 0,
                        "a reopened window never restores the previous window's lookup");
                }
                finally { restarted.Close(); }
                window.Activate();
                await Task.Delay(200);
                Capture((FrameworkElement)window.Content, "v0.6.0-oxford.png");
                Check(window.FindName("SearchButton") == null, "toolbar has no search button");
                Check(window.FindName("ForwardButton") is Button, "toolbar includes forward navigation");
                Check(window.FindName("SettingsButton") is Button, "toolbar includes dictionary settings");
                Check(window.FindName("ResultsScroll") is ScrollViewer, "results use one outer scroll area");

                Edit("banana");
                Press(Key.Enter);
                await Idle();
                Check(Field<string>("displayedWord") == "banana", "Enter without selection navigates to input");
                Check(input.IsEnabled && input.IsKeyboardFocused, "Enter retains input focus after lookup");
                Check(await Script("document.body.innerText.includes('banana')") == "true",
                    "banana actually rendered in WebView2 DOM");

                Edit("appl");
                await WaitUntil(() => popup.IsOpen, "appl suggestions");
                Check(list.SelectedIndex == -1, "suggestions initially unselected");
                Press(Key.Down);
                Check(list.SelectedIndex == 0 && input.IsKeyboardFocused,
                    "Down selects first item and retains input focus");
                Press(Key.Up);
                Check(list.SelectedIndex == list.Items.Count - 1, "Up wraps to last item");
                string selected = (string)list.SelectedItem;
                Press(Key.Enter);
                await Idle();
                Check(Field<string>("displayedWord") == selected, "Enter navigates to selected candidate");

                Edit("appl");
                await WaitUntil(() => popup.IsOpen, "mouse suggestions");
                string clicked = list.Items.Cast<string>().First(w => w != Field<string>("displayedWord"));
                var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(clicked);
                int routedToWindow = 0;
                MouseButtonEventHandler trace = (_, _) => routedToWindow++;
                window.AddHandler(UIElement.PreviewMouseDownEvent, trace, true);
                // Route from the real Popup item, including the Window ancestor.
                item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount,
                    MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
                window.RemoveHandler(UIElement.PreviewMouseDownEvent, trace);
                Check(routedToWindow > 0, "Popup candidate preview reaches Window ancestor");
                await Idle();
                Check(Field<string>("displayedWord") == clicked, "candidate mouse-down navigates");

                Edit("appl");
                await WaitUntil(() => popup.IsOpen, "Esc suggestions");
                Press(Key.Escape);
                input.Focus();
                await Task.Delay(250);
                Check(!popup.IsOpen && input.Text == "appl", "Esc preserves input and refocus stays closed");

                Edit("apple");
                await WaitUntil(() => popup.IsOpen, "edited suggestions");
                Check(list.Items.Count <= 12 && list.Items.Cast<string>().Distinct(
                    StringComparer.OrdinalIgnoreCase).Count() == list.Items.Count,
                    "candidates capped at 12 and deduplicated");
                Press(Key.Enter);
                await Idle();
                Check(Field<string>("displayedWord") == "apple", "unselected Enter uses input");

                int historyCount = Field<List<string>>("history").Count;
                Edit("zzzz_no_such_entry_123");
                Press(Key.Enter);
                await Idle();
                Check(Field<string>("displayedWord") == "" &&
                    input.Text == "zzzz_no_such_entry_123" &&
                    Field<List<string>>("history").Count == historyCount &&
                    Field<List<DictionaryResultView>>("sections").All(s => s.Visibility == Visibility.Collapsed),
                    "missing manual lookup clears results and preserves input and history");

                Edit("appl");
                await WaitUntil(() => popup.IsOpen, "Tab suggestions");
                var tab = Press(Key.Tab);
                Check(!popup.IsOpen && !tab.Handled, "Tab closes candidates and permits focus traversal");

                Edit("appl");
                TickSuggestions();
                Edit("ban");
                TickSuggestions();
                Edit("banana");
                TickSuggestions();
                await WaitUntil(() => popup.IsOpen && Field<string>("displayedSuggestionPrefix") == "banana",
                    "latest prefix result");
                Check(list.Items.Cast<string>().All(w => w.StartsWith("banana", StringComparison.OrdinalIgnoreCase)),
                    "rapid replacements publish only latest prefix");

                Edit("appl");
                TickSuggestions();
                Check(Field<bool>("suggestionSearchRunning") && !popup.IsOpen,
                    "Escape test starts before in-flight results are displayed");
                Press(Key.Escape);
                await WaitUntil(() => !Field<bool>("suggestionSearchRunning"), "cancelled prefix search");
                Check(!popup.IsOpen, "Escape cancels in-flight results");

                Edit("apple");
                Press(Key.Enter);
                await Idle();
                Check(Field<string>("displayedWord") == "apple" && !popup.IsOpen,
                    "Enter returns to apple and closes suggestions");

                historyCount = Field<List<string>>("history").Count;
                await Script("document.querySelector('a[href=\"entry://cooking-apple\"]').click(); true");
                await WaitUntil(() => !Field<bool>("busy") && Field<string>("displayedWord") == "cooking-apple",
                    "entry bridge lookup");
                Check(input.Text == "cooking-apple" && !popup.IsOpen &&
                    Field<List<string>>("history").Count == historyCount + 1,
                    "entry link navigates and commits history without suggestions");
                ((Button)window.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Idle();
                Check(Field<string>("displayedWord") == "apple" && input.Text == "apple" &&
                    Field<List<string>>("history").Count == historyCount + 1 && !popup.IsOpen,
                    "Back restores apple without adding history or suggestions");

                int historyIndex = Field<int>("historyIndex");
                await Script("document.querySelector('a[href=\"#apple_idmgs_1\"]').click(); true");
                Check(await Script("location.hash === '#apple_idmgs_1'") == "true" &&
                    Field<int>("historyIndex") == historyIndex, "Idioms anchor stays on page and out of history");
                Check(await Script("document.styleSheets.length > 0 && Array.from(document.images).some(i => i.naturalWidth > 0)") == "true",
                    "Oxford styles and images loaded from MDD");

                Edit("appl");
                TickSuggestions();
                Press(Key.F9);
                await Idle();
                await WaitUntil(() => !Field<bool>("suggestionSearchRunning"), "F9 cancelled search");
                Check(await Script("document.body.innerText.includes('WebView2')") == "true" &&
                    Field<int>("historyIndex") == historyIndex && !popup.IsOpen,
                    "F9 renders diagnostic without history or delayed suggestions");
                await TestMultipleDictionaries(args[0]);
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine(ex);
            }
            finally
            {
                Console.WriteLine("Checks: " + checks + "; Failures: " + failures);
                string testSettings = Field<string>("settingsPath");
                if (File.Exists(testSettings)) File.Delete(testSettings);
                window.Close();
                app.Shutdown();
            }
        };
        app.Run(window);
        return failures == 0 ? 0 : 1;
    }

    private static T Field<T>(string name, MainWindow target = null) => (T)typeof(MainWindow)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target ?? window);

    private static void Edit(string value)
    {
        window.Activate();
        input.Focus();
        input.Clear();
        input.Text = value;
    }

    private static KeyEventArgs Press(Key key)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input),
            Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        input.RaiseEvent(e);
        return e;
    }

    private static void TickSuggestions() => typeof(MainWindow)
        .GetMethod("SuggestionTimer_Tick", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(window, new object[] { null, EventArgs.Empty });

    private static Task<string> Script(string code) => Field<List<DictionaryResultView>>("sections")[0]
        .Browser.CoreWebView2.ExecuteScriptAsync(code);

    private static Task<string> ScriptAt(int index, string code) => Field<List<DictionaryResultView>>("sections")[index]
        .Browser.CoreWebView2.ExecuteScriptAsync(code);

    private static Task Configure(params string[] paths) => (Task)typeof(MainWindow)
        .GetMethod("ApplyDictionaryPathsAsync", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(window, new object[] { paths });

    private static void Click(string name) => ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task TestMultipleDictionaries(string realOxford)
    {
        string directory = Path.Combine(Path.GetTempPath(), "MdictViewer-fixtures-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string companion = Path.Combine(Path.GetDirectoryName(realOxford), "牛津高阶英汉双解词典（汉英+同义词+例句反查）V3_1.mdx");
            if (File.Exists(companion))
            {
                await Configure(realOxford, companion);
                var realSections = Field<List<DictionaryResultView>>("sections");
                string companionHtml = await Task.Run(() => realSections[1].Service.Lookup("apple"));
                Check(realSections.Count == 2 && realSections.All(s => s.IsAvailable) &&
                    await ScriptAt(0, "document.body.innerText.includes('apple')") == "true" &&
                    (string.IsNullOrWhiteSpace(companionHtml) ? realSections[1].Visibility == Visibility.Collapsed :
                        await ScriptAt(1, "document.body.innerText.trim().length > 0") == "true"),
                    "two actual Oxford MDX files display independent lookup results");
            }
            string a = DictionaryFixture.Create(directory, "A", "#ba5b49", "only-a");
            string b = DictionaryFixture.Create(directory, "B", "#3271ae", "only-b");
            string localDirectory = Path.Combine(directory, "standalone");
            Directory.CreateDirectory(localDirectory);
            string local = DictionaryFixture.Create(localDirectory, "Local", "black", "only-local");
            File.Delete(Path.ChangeExtension(local, ".mdd"));
            File.WriteAllText(Path.Combine(localDirectory, "shared.css"), ".marker{color:rgb(11,22,33)}");
            Edit("apple");
            await Configure(a, local);
            Check(await ScriptAt(0, "getComputedStyle(document.querySelector('.marker')).color") == "\"rgb(186, 91, 73)\"" &&
                await ScriptAt(1, "getComputedStyle(document.querySelector('.marker')).color") == "\"rgb(11, 22, 33)\"",
                "standalone CSS loads in WebView2 and stays isolated from another dictionary's MDD");
            string settings = Path.Combine(directory, "settings.json");
            DictionarySettings.Save(settings, new[] { a, b });
            Check(DictionarySettings.Load(settings).SequenceEqual(new[] { a, b }), "settings round-trip preserves ordered MDX paths");
            Check(Rejects(() => DictionarySettings.ValidatePaths(new[] { a, a.ToUpperInvariant() })), "duplicate paths rejected case-insensitively");
            Check(Rejects(() => DictionarySettings.ValidatePaths(new[] { Path.Combine(directory, "missing.mdx") })), "missing path rejected");
            var eight = new List<string> { a, b };
            for (int i = 2; i < 9; i++)
            {
                string copy = Path.Combine(directory, "copy" + i + ".mdx");
                File.Copy(a, copy);
                if (i < 8) eight.Add(copy);
                else Check(Rejects(() => DictionarySettings.ValidatePaths(eight.Append(copy))), "ninth dictionary rejected");
            }
            Check(DictionarySettings.ValidatePaths(eight).Count == 8, "eight dictionary paths accepted");
            var dialog = new SettingsWindow(new[] { a });
            var rows = (StackPanel)dialog.FindName("PathRows");
            var plus = (Button)dialog.FindName("AddButton");
            Check(plus.Visibility == Visibility.Visible, "valid last path reveals plus below rows");
            plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(rows.Children.Count == 2 && plus.Visibility == Visibility.Collapsed, "plus adds one empty row then hides");
            ((TextBox)((Grid)rows.Children[1]).Children[0]).Text = b;
            Check(plus.Visibility == Visibility.Visible, "completing second MDX reveals next plus");
            ((Button)((Grid)rows.Children[1]).Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(rows.Children.Count == 1, "path row can be removed");
            dialog.Close();
            Check(dialog.ResultPaths == null && DictionarySettings.Load(settings).SequenceEqual(new[] { a, b }), "cancel leaves saved configuration unchanged");
            dialog = new SettingsWindow(eight);
            Check(((Button)dialog.FindName("AddButton")).Visibility == Visibility.Collapsed, "plus hidden at eight paths");
            dialog.Owner = window;
            dialog.Width = dialog.MinWidth;
            dialog.Height = dialog.MinHeight;
            dialog.Show();
            dialog.UpdateLayout();
            TestWindowLimits(dialog, 1000, 700, "settings window");
            var pathScroll = VisualChild<ScrollViewer>((FrameworkElement)dialog.Content);
            var save = (Button)dialog.FindName("SaveButton");
            Check(pathScroll != null && pathScroll.ScrollableHeight > 0 &&
                save.TranslatePoint(new Point(), dialog).Y >= pathScroll.TranslatePoint(new Point(0, pathScroll.ActualHeight), dialog).Y,
                "eight rounded path rows scroll without overlapping footer in smallest settings window");
            Capture((FrameworkElement)dialog.Content, "settings-style-eight-compact.png");
            dialog.Close();
            await TestSettingsWorkflow(a, b);

            Edit("apple");
            await Configure(a, b);
            var sections = Field<List<DictionaryResultView>>("sections");
            var scroll = (ScrollViewer)window.FindName("ResultsScroll");
            Check(sections.Count == 2 && !ReferenceEquals(sections[0].Service, sections[1].Service) &&
                !ReferenceEquals(sections[0].Browser, sections[1].Browser), "dictionaries have independent readers and browsers");
            Check(await ScriptAt(0, "document.body.innerText.includes('A apple')") == "true" &&
                await ScriptAt(1, "document.body.innerText.includes('B APPLE')") == "true", "same lookup renders both dictionary results");
            Check(await ScriptAt(0, "getComputedStyle(document.querySelector('.marker')).color") == "\"rgb(186, 91, 73)\"" &&
                await ScriptAt(1, "getComputedStyle(document.querySelector('.marker')).color") == "\"rgb(50, 113, 174)\"", "same CSS path resolves to each dictionary's own MDD");
            Check(await AudioBytesAt(0) == "ID3-A" && await AudioBytesAt(1) == "ID3-B", "same audio resource path remains isolated");
            await WaitUntil(() => sections.All(s => s.Browser.Height > 900), "intrinsic document height");
            Check(sections[1].TranslatePoint(new Point(), scroll).Y > sections[0].Browser.Height, "result sections stack vertically without overlap");
            double tall = sections[0].Browser.Height;
            await Script("document.getElementById('expansion').style.height='20px';true");
            await WaitUntil(() => sections[0].Browser.Height < tall - 500, "collapsed content shrinks section");
            Check(sections[0].Browser.Height < tall - 500, "dynamic collapse shrinks result height");
            await Script("document.getElementById('expansion').style.height='900px';true");
            await WaitUntil(() => sections[0].Browser.Height > 900, "expanded content grows section");
            double cssHeight = sections[0].Browser.Height;
            await Script("document.styleSheets[0].insertRule('#expansion{height:20px!important}',0);true");
            await WaitUntil(() => sections[0].Browser.Height < cssHeight - 500, "CSSOM collapse height update");
            Check(sections[0].Browser.Height < cssHeight - 500, "CSSOM-only size change updates section without DOM mutation");
            await Script("document.styleSheets[0].deleteRule(0);true");
            await Task.Delay(150);
            await Script("window.testAnimation=document.getElementById('expansion').animate([{height:'900px'},{height:'20px'}],{duration:150,fill:'forwards'});true");
            await WaitUntil(() => sections[0].Browser.Height < cssHeight - 500, "animated collapse height update");
            Check(sections[0].Browser.Height < cssHeight - 500, "Web Animation updates result height without DOM mutation");
            await Script("window.testAnimation.cancel();true");
            await Task.Delay(150);
            await Script("document.body.style.cssText='margin:0;min-height:100vh';true");
            await Task.Delay(300);
            double viewportRelativeHeight = sections[0].Browser.Height;
            await Task.Delay(350);
            Check(Math.Abs(sections[0].Browser.Height - viewportRelativeHeight) <= 2, "viewport-relative CSS does not cause unbounded height feedback");
            await Script("document.body.style.cssText='';true");
            await Task.Delay(150);
            double normalHeight = sections[0].Browser.Height;
            sections[0].Browser.ZoomFactor = 1.5;
            await Task.Delay(300);
            Check(sections[0].Browser.Height > normalHeight * 1.4, "zoom increases host height even when intrinsic CSS height stays fixed");
            sections[0].Browser.ZoomFactor = 1;
            await Task.Delay(150);
            await Script("window.chrome.webview.postMessage({type:'mdict-height',value:'invalid'});true");
            await Script("window.chrome.webview.postMessage({type:'mdict-height',value:-1});true");
            await Task.Delay(100);
            Check(sections[0].Browser.Height > 900, "invalid layout messages cannot change result dimensions");
            await ScriptAt(1, "document.querySelector('a[href=\"#bottom\"]').click();true");
            await WaitUntil(() => scroll.VerticalOffset > 900, "second dictionary anchor scroll");
            Check(Field<List<string>>("history").Count == 1 && Field<int>("historyIndex") == 0, "local anchor scroll leaves unified history unchanged");
            scroll.ScrollToTop();
            await Task.Delay(100);
            await ScriptAt(1, "document.querySelector('a[href=\"#bottom\"]').click();true");
            await Task.Delay(200);
            Check(scroll.VerticalOffset > 900, "same anchor scrolls again after outer viewer moved away");
            scroll.ScrollToTop();
            await Task.Delay(100);
            await Script("window.dispatchEvent(new WheelEvent('wheel',{deltaY:150,cancelable:true}));true");
            await WaitUntil(() => scroll.VerticalOffset > 0, "outer wheel scroll");
            Check(scroll.VerticalOffset > 0, "wheel over dictionary scrolls unified results");

            Edit("app");
            await WaitUntil(() => popup.IsOpen, "merged suggestions");
            Check(list.Items.Cast<string>().SequenceEqual(new[] { "apple", "apple-pie", "appraise" }), "suggestions preserve config order and deduplicate original spelling");
            Press(Key.Escape);
            Edit("banana"); Press(Key.Enter); await Idle();
            Check(await ScriptAt(0, "document.body.innerText.includes('A banana')") == "true" &&
                await ScriptAt(1, "document.body.innerText.includes('B banana')") == "true" && input.IsKeyboardFocused,
                "Enter updates all dictionaries and retains focus");
            await ScriptAt(1, "document.querySelector('a[href^=\"entry://\"]').click();true");
            await WaitUntil(() => !Field<bool>("busy") && Field<string>("displayedWord") == "cooking-apple", "multi-dictionary entry lookup");
            Check(await ScriptAt(0, "document.body.innerText.includes('A cooking-apple')") == "true" &&
                Field<List<string>>("history").Count == 3, "link in second result updates first and unified history");
            Click("BackButton"); await Idle();
            Check(Field<string>("displayedWord") == "banana" && ((Button)window.FindName("ForwardButton")).IsEnabled, "Back enables forward navigation");
            Click("ForwardButton"); await Idle();
            Check(Field<string>("displayedWord") == "cooking-apple" && !((Button)window.FindName("ForwardButton")).IsEnabled,
                "Forward restores all results and disables at end");
            Click("BackButton"); await Idle();
            Edit("only-a"); Press(Key.Enter); await Idle();
            Check(await ScriptAt(0, "document.body.innerText.includes('A only-a')") == "true" &&
                sections[0].Visibility == Visibility.Visible && sections[1].Visibility == Visibility.Collapsed &&
                !((Button)window.FindName("ForwardButton")).IsEnabled && !Field<List<string>>("history").Contains("cooking-apple"),
                "partial match hides missing dictionary and new lookup discards forward branch");
            int count = Field<List<string>>("history").Count;
            var toggle = VisualChild<Button>(sections[0]);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(sections[0].Visibility == Visibility.Visible && sections[0].Browser.Visibility == Visibility.Collapsed,
                "collapse keeps filename header and hides result body");
            window.UpdateLayout();
            await Task.Delay(80);
            Check((string)toggle.ToolTip == "展开查询结果" && scroll.ScrollableHeight == 0,
                "collapsed arrow and layout remove the result's scroll space");
            Capture((FrameworkElement)window.Content, "folded-result.png");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(sections[0].Browser.Visibility == Visibility.Visible, "toggle expands existing result again");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Edit("apple"); Press(Key.Enter);
            Check(sections.All(s => s.Visibility == Visibility.Collapsed), "Enter immediately clears all prior containers");
            await Idle();
            Check(sections.All(s => s.Visibility == Visibility.Visible && s.Browser.Visibility == Visibility.Visible),
                "new query expands all matching results");
            count = Field<List<string>>("history").Count;
            Edit("no-such-word"); Press(Key.Enter); await Idle();
            Check(Field<List<string>>("history").Count == count && Field<string>("displayedWord") == "" &&
                sections.All(s => s.Visibility == Visibility.Collapsed) && input.IsKeyboardFocused,
                "all misses clear displayed content without adding history or losing input focus");
            Edit("apple"); Press(Key.Enter); await Idle();
            count = Field<List<string>>("history").Count;
            Edit("unfinished-input");
            await Script("let missing=document.createElement('a');missing.href='entry://absent';document.body.appendChild(missing);missing.click();true");
            await Task.Delay(50); await Idle();
            Check(input.Text == "apple" && Field<List<string>>("history").Count == count && sections.All(s => s.Visibility == Visibility.Collapsed),
                "failed entry link clears results and keeps history unchanged");
            Edit("image-only"); Press(Key.Enter); await Idle();
            Check(sections.All(s => s.Visibility == Visibility.Visible) && await Script("document.body.innerText.trim().length") == "0",
                "image-only HTML result displays without requiring visible text");
            Edit("blank-result"); Press(Key.Enter); await Idle();
            Check(sections.All(s => s.Visibility == Visibility.Collapsed), "whitespace-only results do not display dictionary containers");
            Edit("apple"); Press(Key.Enter); await Idle();
            Edit(""); Press(Key.Enter); await Idle();
            Check(sections.All(s => s.Visibility == Visibility.Collapsed) && Field<string>("displayedWord") == "",
                "Enter on empty input clears existing results");
            await Configure(a);
            Edit("apple"); Press(Key.Enter); await Idle();
            count = Field<List<string>>("history").Count;
            Field<List<DictionaryResultView>>("sections")[0].Controller.Dispose();
            Edit("unfinished-input");
            await (Task)typeof(MainWindow).GetMethod("LookupAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, new object[] { "banana", true, -1, false });
            Check(input.Text == "apple", "entry display failure restores the previous query text");
            Check(Field<string>("displayedWord") == "" && Field<List<string>>("history").Count == count &&
                Field<List<DictionaryResultView>>("sections").All(s => s.Visibility == Visibility.Collapsed),
                "entry display failure leaves results empty and history unchanged");
            await Configure(a);
            Field<List<DictionaryResultView>>("sections")[0].Controller.Dispose();
            Edit("banana"); Press(Key.Enter); await Idle();
            Check(input.Text == "banana", "manual display failure preserves the edited query text");
            for (int i = 0; i < 5; i++)
            {
                var abandoned = new DictionaryResultView(a);
                Task opening = abandoned.OpenAsync();
                abandoned.Dispose();
                await opening;
                await Task.Delay(50);
                Check(!abandoned.Service.IsOpen, "closing during async open does not reopen abandoned reader " + i);
            }
            await Configure(a, b);
            Edit("apple"); Press(Key.Enter); await Idle();
            await ReplaceDuringResourceRead(b);

            await Configure(eight.ToArray());
            Check(Field<List<DictionaryResultView>>("sections").Count == 8 &&
                Field<List<DictionaryResultView>>("sections").All(s => s.IsAvailable), "eight dictionary browsers initialize independently");
            var old = Field<List<DictionaryResultView>>("sections").ToArray();
            await Configure(a, Path.Combine(directory, "broken.mdx"));
            Check(old.All(s => !s.IsAvailable) && Field<List<DictionaryResultView>>("sections")[0].IsAvailable &&
                !Field<List<DictionaryResultView>>("sections")[1].IsAvailable, "reconfiguration releases old views and isolates dictionary open failure");
            Edit("apple"); Press(Key.Enter); await Idle();
            Check(Field<string>("displayedWord") == "apple", "working dictionary remains searchable beside failed dictionary");
            for (int i = 0; i < 55; i++) { Edit("term" + i.ToString("00")); Press(Key.Enter); await Idle(); }
            Check(Field<List<string>>("history").Count == 50 && Field<List<string>>("history")[0] == "term05", "history retains latest fifty successful searches");
            await Configure(a, b);
            Edit("apple"); Press(Key.Enter); await Idle();
            await ScriptAt(0, "document.getElementById('expansion').style.height='30px';true");
            await ScriptAt(1, "document.getElementById('expansion').style.height='30px';true");
            await WaitUntil(() => Field<List<DictionaryResultView>>("sections").All(s => s.Browser.Height < 500), "compact results");
            await Task.Delay(200);
            Capture((FrameworkElement)window.Content, "v0.6.0-desktop.png");
            window.Width = 620; window.Height = 450;
            await Task.Delay(250);
            Capture((FrameworkElement)window.Content, "v0.6.0-compact.png");
            Check(input.ActualWidth > 300 && ((Button)window.FindName("SettingsButton")).TranslatePoint(new Point(40, 0), window).X < window.ActualWidth,
                "compact toolbar fits input and three icons without overlap");
            await Configure();
            Check(Field<List<DictionaryResultView>>("sections").Count == 0 && !((Button)window.FindName("BackButton")).IsEnabled,
                "clearing dictionaries resets results and history");
            string browserFolder = Environment.GetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER");
            try
            {
                Environment.SetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER",
                    Path.Combine(directory, "missing-browser"));
                Edit("");
                await Configure(a);
                Press(Key.F9); await Idle();
                Check(Field<List<DictionaryResultView>>("sections").All(s => s.Visibility == Visibility.Collapsed),
                    "failed diagnostic on a configured dictionary leaves no empty container");
                await Configure();
                Press(Key.F9); await Idle();
                Check(((TextBlock)window.FindName("StatusText")).Text.StartsWith("WebView2 自测失败："),
                    "missing browser reproduces diagnostic initialization failure");
                Check(Field<DictionaryResultView>("diagnosticView") == null,
                    "failed diagnostic initialization releases its view for retry");
                await Configure(Path.Combine(directory, "broken.mdx"));
                Press(Key.F9); await Idle();
                Check(((TextBlock)window.FindName("EmptyText")).Visibility == Visibility.Collapsed,
                    "diagnostic failure does not report empty configuration beside an unavailable dictionary");
                await Configure();
            }
            finally { Environment.SetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", browserFolder); }
            Press(Key.F9); await Idle();
            var diagnostic = Field<DictionaryResultView>("diagnosticView");
            Check(diagnostic != null && diagnostic.Browser.CoreWebView2 != null &&
                await diagnostic.Browser.CoreWebView2.ExecuteScriptAsync(
                    "document.body.innerText.includes('WebView2 显示测试')") == "true",
                "F9 retries successfully after browser initialization failure without configured dictionaries");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static bool Rejects(Action action)
    {
        try { action(); return false; } catch (ArgumentException) { return true; }
    }

    private static async Task<string> AudioBytesAt(int index)
    {
        var core = Field<List<DictionaryResultView>>("sections")[index].Browser.CoreWebView2;
        var completion = new TaskCompletionSource<string>();
        EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2WebResourceResponseReceivedEventArgs> handler = async (_, e) =>
        {
            if (!e.Request.Uri.Contains("/sound/shared.mp3")) return;
            try
            {
                using var content = await e.Response.GetContentAsync();
                using var reader = new StreamReader(content);
                completion.TrySetResult(await reader.ReadToEndAsync());
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        };
        core.WebResourceResponseReceived += handler;
        try
        {
            await core.ExecuteScriptAsync("window.testAudio = new Audio('/sound/shared.mp3');testAudio.load();true");
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { core.WebResourceResponseReceived -= handler; }
    }

    private static async Task ReplaceDuringResourceRead(string replacement)
    {
        var section = Field<List<DictionaryResultView>>("sections")[0];
        object sync = section.Service.GetType().GetField("sync", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(section.Service);
        var held = new TaskCompletionSource<bool>();
        using var release = new ManualResetEventSlim(false);
        var blocker = Task.Run(() =>
        {
            lock (sync) { held.TrySetResult(true); release.Wait(TimeSpan.FromSeconds(10)); }
        });
        await held.Task;
        var requested = new TaskCompletionSource<bool>();
        section.Browser.CoreWebView2.WebResourceRequested += (_, e) =>
        {
            if (e.Request.Uri.Contains("slow-test")) requested.TrySetResult(true);
        };
        try
        {
            await Script("let slow=document.createElement('link');slow.rel='stylesheet';slow.href='shared.css?slow-test';document.head.appendChild(slow);true");
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var fallbackRelease = Task.Run(async () => { await Task.Delay(500); release.Set(); });
            var configure = Configure(replacement);
            release.Set();
            await configure;
            await blocker;
            await fallbackRelease;
            await Task.Delay(100);
            Check(!section.IsAvailable && Field<List<DictionaryResultView>>("sections").Single().IsAvailable,
                "reconfiguration during blocked browser resource deferral completes without stale callbacks");
        }
        finally { release.Set(); await blocker; }
    }

    private static async Task TestSettingsWorkflow(string a, string b)
    {
        _ = window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            var dialog = window.OwnedWindows.OfType<SettingsWindow>().Single();
            try
            {
                var rows = (StackPanel)dialog.FindName("PathRows");
                while (rows.Children.Count > 1)
                    ((Button)((Grid)rows.Children[^1]).Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var first = (TextBox)((Grid)rows.Children[0]).Children[0];
                first.Text = "Z:\\missing-test-dictionary.mdx";
                ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(dialog.IsVisible && ((TextBlock)dialog.FindName("ErrorText")).Text.Length > 0,
                    "settings save rejects invalid path without closing dialog");
                first.Text = a;
                ((Button)dialog.FindName("AddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((TextBox)((Grid)rows.Children[1]).Children[0]).Text = b;
                dialog.UpdateLayout();
                TestSettingsAppearance(dialog);
                Capture((FrameworkElement)dialog.Content, "v0.6.0-settings.png");
                ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch { dialog.Close(); throw; }
        }));
        Click("SettingsButton");
        await Idle();
        Check(Field<List<string>>("configuredPaths").SequenceEqual(new[] { a, b }) &&
            DictionarySettings.Load(Field<string>("settingsPath")).SequenceEqual(new[] { a, b }),
            "settings button saves ordered paths and reloads real result sections");
        int historyCount = Field<List<string>>("history").Count;
        _ = window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            var dialog = window.OwnedWindows.OfType<SettingsWindow>().Single();
            ((TextBox)((Grid)((StackPanel)dialog.FindName("PathRows")).Children[0]).Children[0]).Text = b;
            dialog.DialogResult = false;
        }));
        Click("SettingsButton");
        Check(Field<List<string>>("configuredPaths").SequenceEqual(new[] { a, b }) &&
            DictionarySettings.Load(Field<string>("settingsPath")).SequenceEqual(new[] { a, b }) &&
            Field<List<string>>("history").Count == historyCount,
            "cancelling edited settings leaves active readers, history and persisted paths unchanged");
    }

    private static void TestSettingsAppearance(SettingsWindow dialog)
    {
        var rows = (StackPanel)dialog.FindName("PathRows");
        var input = (TextBox)((Grid)rows.Children[0]).Children[0];
        var add = (Button)dialog.FindName("AddButton");
        var browse = (Button)((Grid)rows.Children[0]).Children[1];
        var remove = (Button)((Grid)rows.Children[0]).Children[2];
        var heading = (TextBlock)((Grid)dialog.Content).Children[0];
        Check(dialog.FontSize == 15 && input.FontSize == 15 && heading.FontSize == 15 &&
            ((Button)dialog.FindName("SaveButton")).FontSize == 15 &&
            ((Button)dialog.FindName("CancelButton")).FontSize == 15,
            "settings heading path fields and action buttons consistently use size 15");
        Check(dialog.Icon != null, "settings window shares the drawn M icon");
        var buttons = new[] { add, browse, remove, (Button)dialog.FindName("SaveButton"),
            (Button)dialog.FindName("CancelButton") };
        Check(buttons.All(button => button.Template.FindName("ButtonBorder", button) is Border border &&
            border.CornerRadius == new CornerRadius(6)), "settings buttons have six-pixel rounded templates");
        Check(input.Template.FindName("InputBorder", input) is Border inputBorder &&
            inputBorder.CornerRadius == new CornerRadius(6), "settings text boxes have six-pixel rounded templates");
        var icons = new[] { add, browse, remove };
        Check(icons.All(button => button.Width == 40 && button.Height == 40 &&
            button.Background is SolidColorBrush brush && brush.Color == Color.FromRgb(245, 242, 233)),
            "settings icon buttons match toolbar dimensions and background");
        Check(icons.All(button => button.Content is Image icon && icon.Width == 24 && icon.Height == 24 &&
            icon.Source is DrawingImage drawing && drawing.Drawing.Bounds == new Rect(0, 0, 24, 24)),
            "add remove and browse use consistently framed vector images instead of font glyphs");
        Check(icons.All(button => button.Content is Image { Source: DrawingImage { Drawing: DrawingGroup group } } &&
            group.Children.OfType<GeometryDrawing>().Any(drawing => drawing.Pen is Pen pen &&
                pen.Thickness >= 2.5 && pen.Brush is SolidColorBrush brush && brush.Color == Color.FromRgb(186, 91, 73) &&
                pen.StartLineCap == PenLineCap.Round && pen.EndLineCap == PenLineCap.Round)),
            "drawn settings icons use thick rounded toolbar-colored strokes");
        Check(add.Content is Image plus && remove.Content is Image minus &&
            !ReferenceEquals(plus.Source, minus.Source), "add and remove have distinct drawn symbols");
        input.Focus();
        dialog.UpdateLayout();
        Check(input.IsKeyboardFocused && input.Template.FindName("InputBorder", input) is Border focused &&
            focused.BorderBrush is SolidColorBrush focusBrush && focusBrush.Color == Color.FromRgb(50, 113, 174),
            "rounded path input retains keyboard focus with visible blue border");
        var save = (Button)dialog.FindName("SaveButton");
        save.Focus();
        dialog.UpdateLayout();
        Check(save.IsKeyboardFocused && save.Template.FindName("ButtonBorder", save) is Border buttonBorder &&
            buttonBorder.BorderBrush is SolidColorBrush buttonFocus && buttonFocus.Color == Color.FromRgb(50, 113, 174),
            "rounded action button has visible keyboard focus");
        input.Focus();
    }

    private static T VisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = VisualChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private static void TestWindowLimits(Window target, double widthLimit, double heightLimit, string description)
    {
        bool bounded = target.MaxWidth > 0 && target.MaxWidth <= Math.Min(widthLimit, SystemParameters.WorkArea.Width) + 1 &&
            target.MaxHeight > 0 && target.MaxHeight <= Math.Min(heightLimit, SystemParameters.WorkArea.Height) + 1;
        Check(bounded, description + " is bounded by the agreed size and screen work area");
        if (!bounded) return;
        double width = target.Width;
        double height = target.Height;
        double left = target.Left;
        double minWidth = target.MinWidth;
        double minHeight = target.MinHeight;
        double maxWidth = target.MaxWidth;
        double maxHeight = target.MaxHeight;
        try
        {
            target.Width = widthLimit + 300;
            target.Height = heightLimit + 300;
            target.UpdateLayout();
            Check(target.ActualWidth <= target.MaxWidth + 1 && target.ActualHeight <= target.MaxHeight + 1,
                description + " rejects resizing beyond its maximum dimensions");
            target.WindowState = WindowState.Maximized;
            target.UpdateLayout();
            Check(target.ActualWidth <= target.MaxWidth + 1 && target.ActualHeight <= target.MaxHeight + 1,
                description + " also obeys its size limits when maximized");
            if (target == window)
                Check(Math.Abs(target.ActualWidth - SystemParameters.WorkArea.Width) < 1 &&
                    Math.Abs(target.ActualHeight - SystemParameters.WorkArea.Height) < 1,
                    "maximized main window fills the available screen");
            target.WindowState = WindowState.Normal;
            // Simulate a smaller work area's previous constraints without changing desktop settings.
            target.MinWidth = Math.Max(0, minWidth - 40);
            target.MinHeight = Math.Max(0, minHeight - 40);
            target.Left = left + 1;
            target.UpdateLayout();
            Check(target.MinWidth == minWidth && target.MinHeight == minHeight,
                description + " restores its original minimum layout size after moving back");
            target.MaxWidth = widthLimit + 100;
            target.MaxHeight = heightLimit + 100;
            SendMessage(new System.Windows.Interop.WindowInteropHelper(target).Handle, 0x001A, new IntPtr(0x002F), IntPtr.Zero);
            Check(target.MaxWidth <= Math.Min(widthLimit, SystemParameters.WorkArea.Width) + 1 &&
                target.MaxHeight <= Math.Min(heightLimit, SystemParameters.WorkArea.Height) + 1,
                description + " refreshes its bounds on a work-area notification without moving");
        }
        finally
        {
            target.WindowState = WindowState.Normal;
            target.MaxWidth = maxWidth; target.MaxHeight = maxHeight;
            target.MinWidth = minWidth; target.MinHeight = minHeight;
            target.Left = left; target.Width = width; target.Height = height;
            target.UpdateLayout();
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private static void TestAppIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/MdictViewer;component/Assets/App.ico"));
            using var stream = resource.Stream;
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(new[] { 16, 24, 32, 48, 64, 128, 256 }.All(size => decoder.Frames.Any(frame => frame.PixelWidth == size)),
                "application icon includes small and high-DPI frames");
            var bitmap = new FormatConvertedBitmap(decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First(), PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            int background = 0;
            int letter = 0;
            int smoothEdges = 0;
            int minX = bitmap.PixelWidth;
            int minY = bitmap.PixelHeight;
            int maxX = -1;
            int maxY = -1;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i] == 184 && pixels[i + 1] == 197 && pixels[i + 2] == 221 && pixels[i + 3] == 255) background++;
                if (pixels[i] == 152 && pixels[i + 1] == 104 && pixels[i + 2] == 16 && pixels[i + 3] == 255)
                {
                    letter++;
                    int x = i / 4 % bitmap.PixelWidth;
                    int y = i / 4 / bitmap.PixelWidth;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
                if (pixels[i + 3] > 0 && pixels[i + 3] < 255) smoothEdges++;
            }
            Check(pixels[3] == 0 && background > 1000 && letter > 1000 && smoothEdges > 100,
                "M icon preserves both requested colors transparent corners and antialiased circle edges");
            Check(pixels[(5 * bitmap.PixelWidth + 128) * 4 + 3] == 255,
                "logo circle fills more of its frame with reduced outer padding");
            Check(maxX - minX + 1 >= 162 && maxY - minY + 1 >= 142,
                "drawn M is visibly enlarged in both dimensions");
        }
        catch (IOException) { Check(false, "application icon resource is present and decodable"); }
    }

    private static void Capture(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(246, 247, 249)), null, bounds);
            drawing.DrawRectangle(new VisualBrush(element), null, bounds);
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, name));
        encoder.Save(stream);
    }

    private static async Task Idle() => await WaitUntil(() => !Field<bool>("busy"), "lookup completion");

    private static async Task WaitUntil(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(description);
            await Task.Delay(25);
        }
    }

    private static void Check(bool passed, string description)
    {
        checks++;
        Console.WriteLine((passed ? "PASS: " : "FAIL: ") + description);
        if (!passed) failures++;
    }
}
