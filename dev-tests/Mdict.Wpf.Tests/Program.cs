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
        if (args.Length == 1 && args[0] == "--settings") return SettingsTests.Run();
        if (args.Length == 2 && args[0] == "--published") return PublishedAppTests.Run(args[1]);
        Console.WriteLine("Runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        Console.WriteLine("WPF: " + typeof(System.Windows.Application).Assembly.Location);
        if (args.Length < 1 || args.Length > 2 || !File.Exists(args[0]) ||
            (args.Length == 2 && !File.Exists(args[1])))
        {
            Console.Error.WriteLine("Usage: dotnet run --project dev-tests/Mdict.Wpf.Tests -- <dictionary.mdx> [second-dictionary.mdx]");
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
                Check(double.IsPositiveInfinity(window.MaxWidth) && double.IsPositiveInfinity(window.MaxHeight),
                    "main window has no application maximum dimensions");
                double originalWidth = window.Width, originalHeight = window.Height;
                try
                {
                    window.Width = SystemParameters.WorkArea.Width + 100;
                    window.Height = SystemParameters.WorkArea.Height + 100;
                    window.UpdateLayout();
                    Check(window.ActualWidth > SystemParameters.WorkArea.Width && window.ActualHeight > SystemParameters.WorkArea.Height,
                        "main window can exceed the former work-area size cap");
                    SendMessage(new System.Windows.Interop.WindowInteropHelper(window).Handle, 0x001A, new IntPtr(0x002F), IntPtr.Zero);
                    Check(double.IsPositiveInfinity(window.MaxWidth) && double.IsPositiveInfinity(window.MaxHeight),
                        "work-area changes do not restore main window maximum dimensions");
                }
                finally { window.Width = originalWidth; window.Height = originalHeight; }
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
                Check(!Field<List<DictionaryResultView>>("sections")[0].Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled &&
                    input.ReadLocalValue(FrameworkElement.ContextMenuProperty) == null,
                    "main browser and search input disable their default context menus");
                Check(Field<List<DictionaryResultView>>("sections")[0].Browser.CoreWebView2.Environment.UserDataFolder ==
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MdictViewer", "WebView2"),
                    "browser cache is kept outside the executable directory");
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

                Edit("appl");
                await WaitUntil(() => popup.IsOpen, "move suggestions");
                SendMessage(new System.Windows.Interop.WindowInteropHelper(window).Handle, 0x0231, IntPtr.Zero, IntPtr.Zero);
                Check(!popup.IsOpen && input.IsKeyboardFocused,
                    "starting a window move or resize closes suggestions even when input retains focus");
                Edit("appl"); TickSuggestions();
                SendMessage(new System.Windows.Interop.WindowInteropHelper(window).Handle, 0x0231, IntPtr.Zero, IntPtr.Zero);
                await WaitUntil(() => !Field<bool>("suggestionSearchRunning"), "move cancelled prefix search");
                Check(!popup.IsOpen && !Field<System.Windows.Threading.DispatcherTimer>("suggestionTimer").IsEnabled,
                    "moving cancels pending suggestions without reopening the popup");
                Edit("appl");
                await WaitUntil(() => popup.IsOpen, "focus suggestions");
                Keyboard.ClearFocus();
                await Task.Delay(250);
                Check(!popup.IsOpen, "losing input focus closes suggestions");
                input.Focus();
                await Task.Delay(250);
                Check(!popup.IsOpen, "refocusing alone does not reopen suggestions");

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
                await TestSelectionAndStatus(args[0]);
                await TestMultipleDictionaries(args[0], args.Length == 2 ? args[1] : null);
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

    private static async Task TestSelectionAndStatus(string dictionary)
    {
        await TestIconMenu(window, "main window");
        await Configure(dictionary);
        Edit("apple"); Press(Key.Enter); await Idle();
        var clipboard = Clipboard.GetDataObject();
        try
        {
            await Script("var t=document.createElement('span');t.id='gesture-test';t.textContent='banana';document.body.append(t);" +
                "var r=document.createRange();r.selectNodeContents(t);var s=getSelection();s.removeAllRanges();s.addRange(r);" +
                "t.dispatchEvent(new MouseEvent('mousedown',{bubbles:true,button:0,clientX:10,clientY:10}));" +
                "t.dispatchEvent(new MouseEvent('mouseup',{bubbles:true,button:0,clientX:40,clientY:10}));");
            await WaitUntil(() => Clipboard.ContainsText() && Clipboard.GetText() == "banana", "drag clipboard copy");
            Check(Field<string>("displayedWord") == "apple" && Field<List<string>>("history").Count == 1,
                "drag copies the selected text without querying or changing history");
            Check(((TextBlock)window.FindName("StatusText")).Text.Contains("已复制所选文字"), "drag reports successful copy");
            string bounds = await Script("var t=document.getElementById('gesture-test');" +
                "t.textContent='before banana after';" +
                "t.style.cssText='position:fixed;left:8px;top:8px;z-index:99999;background:white';" +
                "var r=document.createRange();r.setStart(t.firstChild,7);r.setEnd(t.firstChild,13);" +
                "var b=r.getBoundingClientRect();({x:b.left+b.width/2,y:b.top+b.height/2})");
            using (var coordinates = System.Text.Json.JsonDocument.Parse(bounds))
            {
                double x = coordinates.RootElement.GetProperty("x").GetDouble();
                double y = coordinates.RootElement.GetProperty("y").GetDouble();
                var browser = Field<List<DictionaryResultView>>("sections")[0].Browser;
                window.Activate();
                var point = browser.PointToScreen(new Point(x, y));
                GetCursorPos(out var previousCursor);
                try
                {
                    SetCursorPos((int)point.X, (int)point.Y);
                    mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                    await Task.Delay(100);
                    mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                    mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                    await Task.Delay(700);
                }
                finally { SetCursorPos(previousCursor.X, previousCursor.Y); }
            }
            await WaitUntil(() => !Field<bool>("busy") && Field<string>("displayedWord") == "banana", "double-click lookup");
            Check(Clipboard.GetText() == "banana" && Field<List<string>>("history").Count == 2 &&
                ((TextBlock)window.FindName("StatusText")).Text.StartsWith("已复制并查询：banana"),
                "native double click selects one word in a sentence and uses unified lookup and history");
            await Script("var t=document.createElement('span');t.textContent='__missing_gesture_word__';document.body.append(t);" +
                "var r=document.createRange();r.selectNodeContents(t);getSelection().removeAllRanges();getSelection().addRange(r);" +
                "t.dispatchEvent(new MouseEvent('dblclick',{bubbles:true,button:0}));");
            await WaitUntil(() => ((TextBlock)window.FindName("StatusText")).Text.StartsWith("已复制；未找到词条："), "copied missing word");
            Check(Clipboard.GetText() == "__missing_gesture_word__" && Field<List<string>>("history").Count == 2,
                "failed double-click lookup still copies and does not add history");

            var settings = new SettingsWindow(new[] { dictionary }, Field<string>("settingsPath")) { Owner = window };
            try
            {
                settings.Show();
                await TestIconMenu(settings, "settings window");
                SettingsTests.Add(settings, dictionary);
                var error = (TextBlock)settings.FindName("ErrorText");
                var showStatus = typeof(MainWindow).GetMethod("ShowStatus", BindingFlags.Instance | BindingFlags.NonPublic);
                showStatus.Invoke(window, new object[] { "first message" });
                await Task.Delay(3500);
                showStatus.Invoke(window, new object[] { "second message" });
                SettingsTests.Add(settings, dictionary);
                await Task.Delay(3000);
                Check(((UIElement)window.FindName("StatusPanel")).Visibility == Visibility.Visible && error.Visibility == Visibility.Visible,
                    "new messages restart both six-second countdowns");
                await Task.Delay(3300);
                Check(((UIElement)window.FindName("StatusPanel")).Visibility == Visibility.Collapsed &&
                    ((TextBlock)window.FindName("StatusText")).Text == "" && error.Text == "" && error.Visibility == Visibility.Collapsed,
                    "both message areas clear and collapse after six seconds");
            }
            finally { settings.Close(); }
            Edit("apple"); Press(Key.Enter); await Idle();
        }
        finally { if (clipboard != null) Clipboard.SetDataObject(clipboard, true); else Clipboard.Clear(); }
    }

    private static async Task TestIconMenu(Window target, string description)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(target).Handle;
        var source = System.Windows.Interop.HwndSource.FromHwnd(handle);
        bool menuOpened = false;
        System.Windows.Interop.HwndSourceHook observer = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == 0x0117)
            {
                menuOpened = true;
                PostMessage(hwnd, 0x001F, IntPtr.Zero, IntPtr.Zero);
            }
            return IntPtr.Zero;
        };
        source.AddHook(observer);
        try
        {
            PostMessage(handle, 0x00A1, new IntPtr(3), IntPtr.Zero);
            await Task.Delay(200);
            Check(!menuOpened && (GetWindowLong(handle, -16) & 0x00080000) != 0,
                description + " icon left click suppresses its menu and retains titlebar controls");
        }
        finally { source.RemoveHook(observer); }
    }

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

    private static async Task TestMultipleDictionaries(string realOxford, string companion)
    {
        string directory = Path.Combine(Path.GetTempPath(), "MdictViewer-fixtures-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            if (companion != null)
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
            var eight = new List<string> { a, b };
            for (int i = 2; i < 8; i++)
            {
                string copy = Path.Combine(directory, "copy" + i + ".mdx");
                File.Copy(a, copy);
                eight.Add(copy);
            }
            var dialog = new SettingsWindow(eight, settings);
            dialog.Owner = window;
            dialog.Width = dialog.MinWidth;
            dialog.Height = dialog.MinHeight;
            dialog.Show();
            dialog.UpdateLayout();
            TestWindowLimits(dialog, 500, 500, "settings window");
            int style = GetWindowLong(new System.Windows.Interop.WindowInteropHelper(dialog).Handle, -16);
            Check((style & 0x00010000) == 0 && (style & 0x00040000) != 0,
                "settings disables its native maximize button while retaining edge resizing");
            var pathScroll = VisualChild<ScrollViewer>((FrameworkElement)dialog.Content);
            var controls = (Button)dialog.FindName("BrowseButton");
            var settingsLayout = (Grid)dialog.Content;
            Check(settingsLayout.Margin.Bottom == 8 && settingsLayout.ActualHeight -
                controls.TranslatePoint(new Point(0, controls.ActualHeight), settingsLayout).Y < 1 &&
                ((TextBlock)dialog.FindName("ErrorText")).Visibility == Visibility.Collapsed,
                "settings controls stay close to the bottom without an empty error row");
            Check(pathScroll != null && pathScroll.ScrollableHeight > 0 &&
                controls.TranslatePoint(new Point(), dialog).Y >= pathScroll.TranslatePoint(new Point(0, pathScroll.ActualHeight), dialog).Y,
                "eight filenames scroll without overlapping footer in smallest settings window");
            dialog.Close();
            await TestSettingsWorkflow(a, b);

            Edit("apple");
            await Configure(a, b);
            Press(Key.Enter); await Idle();
            var sections = Field<List<DictionaryResultView>>("sections");
            var scroll = (ScrollViewer)window.FindName("ResultsScroll");
            Check(sections.Count == 2 && !ReferenceEquals(sections[0].Service, sections[1].Service) &&
                !ReferenceEquals(sections[0].Browser, sections[1].Browser), "dictionaries have independent readers and browsers");
            Check(await ScriptAt(0, "document.body.innerText.includes('A apple')") == "true" &&
                await ScriptAt(1, "document.body.innerText.includes('B APPLE')") == "true", "same lookup renders both dictionary results");
            Check(await ScriptAt(0, "getComputedStyle(document.body).fontSize") == "\"14px\"" &&
                await ScriptAt(1, "getComputedStyle(document.body).fontSize") == "\"14px\"" &&
                await ScriptAt(0, "getComputedStyle(document.documentElement).fontSize") == "\"14px\"",
                "dictionary pages share a 14px base font size");
            Check(await ScriptAt(0, "(() => { const box=document.createElement('div'); " +
                "box.innerHTML='<h2>heading</h2><span style=\"font-size:24px\">explicit</span><span style=\"font-size:1.5rem\">relative</span>'; " +
                "document.body.appendChild(box); const ok=parseFloat(getComputedStyle(box.children[0]).fontSize)>14 && " +
                "getComputedStyle(box.children[1]).fontSize==='24px' && getComputedStyle(box.children[2]).fontSize==='21px'; " +
                "box.remove(); return ok; })()") == "true", "base font preserves heading explicit and relative size hierarchy");
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
            var abandoned = new DictionaryResultView(a);
            Task opening;
            var readerLock = abandoned.Service.GetType().GetField("sync", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(abandoned.Service);
            // Prevent background Open from publishing its reader until Dispose has completed.
            lock (readerLock)
            {
                opening = abandoned.OpenAsync();
                abandoned.Dispose();
            }
            await opening;
            Check(!abandoned.Service.IsOpen, "closing before async open publishes prevents an abandoned reader from reopening");
            await Configure(a, b);
            Edit("apple"); Press(Key.Enter); await Idle();
            await ReplaceDuringResourceRead(b);
            Edit("apple"); Press(Key.Enter); await Idle();
            await TestResourceStatistics();

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
            window.Width = 450; window.Height = 450;
            await Task.Delay(250);
            var settingsButton = (Button)window.FindName("SettingsButton");
            var backButton = (Button)window.FindName("BackButton");
            Check(Math.Abs(window.ActualWidth - 450) < 1 && input.ActualWidth >= 100 &&
                input.TranslatePoint(new Point(input.ActualWidth, 0), window).X <= backButton.TranslatePoint(new Point(0, 0), window).X &&
                settingsButton.TranslatePoint(new Point(settingsButton.ActualWidth, 0), window).X < window.ActualWidth,
                "compact toolbar fits input and three icons without overlap");
            await Configure();
            Check(Field<List<DictionaryResultView>>("sections").Count == 0 && !((Button)window.FindName("BackButton")).IsEnabled,
                "clearing dictionaries resets results and history");
            string browserFolder = Environment.GetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER");
            try
            {
                Environment.SetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER",
                    Path.Combine(directory, "missing-browser"));
                var missingRuntimeWindow = new MainWindow(Path.Combine(directory, "missing-runtime.json"));
                try
                {
                    missingRuntimeWindow.Show();
                    await WaitUntil(() => Field<bool>("browserReady", missingRuntimeWindow) &&
                        !Field<bool>("busy", missingRuntimeWindow), "missing runtime startup");
                    Check(((TextBlock)missingRuntimeWindow.FindName("StatusText")).Text.Contains("安装 Microsoft Edge WebView2 Runtime") &&
                        missingRuntimeWindow.FindName("RuntimeHelp") is FrameworkElement help && help.Visibility == Visibility.Visible,
                        "startup explains a missing WebView2 runtime and offers its download link");
                }
                finally { missingRuntimeWindow.Close(); }
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

    private static async Task TestResourceStatistics()
    {
        var section = Field<List<DictionaryResultView>>("sections")[0];
        var core = section.Browser.CoreWebView2;
        var resources = (Mdict.Wpf.WebView.WebResourceHandler)section.Controller.GetType()
            .GetField("resources", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(section.Controller);
        object sync = section.Service.GetType().GetField("sync", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(section.Service);
        const string path = "shared.css";
        {
            var held = new TaskCompletionSource<bool>();
            using var release = new ManualResetEventSlim(false);
            var blocker = Task.Run(() => { lock (sync) { held.SetResult(true); release.Wait(TimeSpan.FromSeconds(10)); } });
            await held.Task;
            var requested = new TaskCompletionSource<bool>();
            var responded = new TaskCompletionSource<bool>();
            EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2WebResourceRequestedEventArgs> request = (_, e) =>
            { if (e.Request.Uri.Contains("statistics-test")) requested.TrySetResult(true); };
            EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2WebResourceResponseReceivedEventArgs> response = (_, e) =>
            { if (e.Request.Uri.Contains("statistics-test")) responded.TrySetResult(true); };
            core.WebResourceRequested += request;
            core.WebResourceResponseReceived += response;
            try
            {
                await core.ExecuteScriptAsync("var statisticsLink=document.createElement('link');statisticsLink.rel='stylesheet';" +
                    "statisticsLink.href='/" + path + "?statistics-test';document.head.appendChild(statisticsLink);true");
                await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
                resources.PreparePage("<p>new page</p>");
                release.Set();
                await responded.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Check(resources.ResourceSuccess == 0 && resources.ResourceFailures == 0 && resources.LastError == "",
                    "late resource response leaves new page statistics empty: " + path);
            }
            finally
            {
                release.Set(); await blocker;
                core.WebResourceRequested -= request;
                core.WebResourceResponseReceived -= response;
            }
        }
    }

    private static async Task TestSettingsWorkflow(string a, string b)
    {
        await Configure(a, b);
        string configurationPath = Field<string>("settingsPath");
        string existing = File.ReadAllText(configurationPath);
        DictionarySettings.Initialize(configurationPath);
        Check(File.ReadAllText(configurationPath) == existing, "initialization never overwrites an existing corrupt configuration");
        File.Delete(configurationPath);
        Check(DictionarySettings.DefaultPath == Path.Combine(AppContext.BaseDirectory, "dictionaries.json"),
            "default configuration is beside the executable");
        Check(DictionarySettings.Load(configurationPath).Count == 0 && !File.Exists(configurationPath),
            "loading a missing configuration does not create it");
        _ = window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            var dialog = window.OwnedWindows.OfType<SettingsWindow>().Single();
            Check(File.Exists(configurationPath) && File.ReadAllText(configurationPath).Trim() == "[]",
                "first Settings click creates an empty configuration before showing the dialog");
            dialog.Close();
        }));
        Click("SettingsButton"); await Idle();
        Check(File.Exists(configurationPath) && File.ReadAllText(configurationPath).Trim() == "[]",
            "closing unchanged Settings leaves the initialized configuration empty");

        var blockedSettings = new MainWindow(Path.GetDirectoryName(configurationPath));
        try
        {
            blockedSettings.Show();
            await WaitUntil(() => Field<bool>("browserReady", blockedSettings) && !Field<bool>("busy", blockedSettings),
                "blocked configuration startup");
            ((Button)blockedSettings.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((TextBlock)blockedSettings.FindName("StatusText")).Text.StartsWith("设置初始化失败：") &&
                !blockedSettings.OwnedWindows.OfType<SettingsWindow>().Any(),
                "configuration creation failure is reported without opening an unusable Settings dialog");
        }
        finally { blockedSettings.Close(); }

        var originalSections = Field<List<DictionaryResultView>>("sections").ToArray();
        string currentWord = Field<string>("displayedWord");
        _ = window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            var dialog = window.OwnedWindows.OfType<SettingsWindow>().Single();
            try
            {
                var dictionaries = (ListBox)dialog.FindName("DictionaryList");
                dictionaries.SelectedIndex = 0;
                ((Button)dialog.FindName("MoveDownButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(DictionarySettings.Load(configurationPath).SequenceEqual(new[] { b, a }),
                    "settings reorder is persisted while the dialog is open");
                Check(Field<List<DictionaryResultView>>("sections").SequenceEqual(originalSections) &&
                    Field<string>("displayedWord") == currentWord, "open Settings does not refresh the main result");
                dialog.UpdateLayout();
                TestSettingsAppearance(dialog);
                dialog.Close();
            }
            catch { dialog.Close(); throw; }
        }));
        Click("SettingsButton");
        await Idle();
        Check(Field<List<string>>("configuredPaths").SequenceEqual(new[] { b, a }) &&
            Field<List<DictionaryResultView>>("sections").Select(section => section.DictionaryPath).SequenceEqual(new[] { b, a }) &&
            Field<string>("displayedWord") == currentWord && originalSections.All(section => !section.IsAvailable),
            "closing changed Settings applies the final order and refreshes the displayed word");
        var appliedSections = Field<List<DictionaryResultView>>("sections").ToArray();
        _ = window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            window.OwnedWindows.OfType<SettingsWindow>().Single().Close()));
        Click("SettingsButton");
        Check(Field<List<DictionaryResultView>>("sections").SequenceEqual(appliedSections),
            "closing unchanged Settings preserves the current readers and result");
        _ = window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            var dialog = window.OwnedWindows.OfType<SettingsWindow>().Single();
            var dictionaries = (ListBox)dialog.FindName("DictionaryList");
            while (dictionaries.Items.Count > 0)
            {
                dictionaries.SelectedIndex = 0;
                ((Button)dialog.FindName("RemoveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Check(DictionarySettings.Load(configurationPath).Count == 0 &&
                Field<List<DictionaryResultView>>("sections").SequenceEqual(appliedSections),
                "deleting all dictionaries saves immediately without refreshing the open main window");
            dialog.Close();
        }));
        Click("SettingsButton"); await Idle();
        Check(Field<List<DictionaryResultView>>("sections").Count == 0 && Field<List<string>>("history").Count == 0 &&
            Field<string>("displayedWord") == "", "closing empty Settings clears results and history");
        Edit("unfinished-input");
        _ = window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            var dialog = window.OwnedWindows.OfType<SettingsWindow>().Single();
            SettingsTests.Add(dialog, a);
            dialog.Close();
        }));
        Click("SettingsButton"); await Idle();
        Check(Field<List<DictionaryResultView>>("sections").Single().Browser.CoreWebView2 == null &&
            Field<string>("displayedWord") == "" && input.Text == "unfinished-input",
            "closing changed Settings never queries text that has not been displayed");
    }

    private static void TestSettingsAppearance(SettingsWindow dialog)
    {
        var dictionaries = (ListBox)dialog.FindName("DictionaryList");
        var browse = (Button)dialog.FindName("BrowseButton");
        var up = (Button)dialog.FindName("MoveUpButton");
        var down = (Button)dialog.FindName("MoveDownButton");
        var remove = (Button)dialog.FindName("RemoveButton");
        Check(dialog.FontSize == 14 && dictionaries.FontSize == 14 && ((TextBlock)dialog.FindName("ErrorText")).FontSize == 14,
            "settings text consistently uses size 14");
        Check(browse.TranslatePoint(new Point(), dialog).X < up.TranslatePoint(new Point(), dialog).X &&
            up.TranslatePoint(new Point(), dialog).X < down.TranslatePoint(new Point(), dialog).X &&
            down.TranslatePoint(new Point(), dialog).X < remove.TranslatePoint(new Point(), dialog).X &&
            VisualChild<TextBox>(dictionaries) == null, "settings shows a filename list and four ordered controls");
        browse.Focus();
        dialog.UpdateLayout();
        Check(browse.IsKeyboardFocused && browse.Template.FindName("ButtonBorder", browse) is Border border &&
            border.BorderBrush is SolidColorBrush brush && brush.Color == Color.FromRgb(50, 113, 174),
            "settings controls remain keyboard accessible with visible focus");
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point point);
    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    private static void TestAppIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/MdictViewer;component/Assets/App.ico"));
        using var stream = resource.Stream;
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Check(decoder.Frames.Count > 0, "application icon resource loads successfully");
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
