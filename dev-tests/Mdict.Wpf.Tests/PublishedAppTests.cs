using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;

// Exercise the actual bundle in a directory containing no companion DLLs.
internal static class PublishedAppTests
{
    private static int checks;

    public static int Run(string sourceExe)
    {
        string directory = Path.Combine(Path.GetTempPath(), "MdictViewer-published-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        Process process = null;
        try
        {
            string appDirectory = Path.Combine(directory, "app");
            string workingDirectory = Path.Combine(directory, "working");
            Directory.CreateDirectory(appDirectory);
            Directory.CreateDirectory(workingDirectory);
            string exe = Path.Combine(appDirectory, "MdictViewer.exe");
            File.Copy(sourceExe, exe);
            string configuration = Path.Combine(appDirectory, "dictionaries.json");
            string dictionary = DictionaryFixture.Create(directory, "便携词典", "green", "only-portable");
            process = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = workingDirectory });
            var main = WaitElement(() => process.MainWindowHandle == IntPtr.Zero ? null :
                AutomationElement.FromHandle(process.MainWindowHandle), "published main window");
            WaitElement(() => Find(main, "StatusText")?.Current.Name.Contains("尚未设置词典") == true ? main : null,
                "empty startup");
            Check(!File.Exists(configuration) && Directory.GetFiles(appDirectory).Length == 1,
                "EXE runs alone and does not create configuration at startup");

            Invoke(Find(main, "SettingsButton"));
            var settings = SettingsWindow(process.Id);
            Check(File.ReadAllText(configuration).Trim() == "[]", "first Settings creates configuration next to EXE");
            Invoke(Find(settings, "BrowseButton"));
            var picker = WaitElement(() => AutomationElement.RootElement.FindFirst(TreeScope.Descendants,
                new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                    new PropertyCondition(AutomationElement.NameProperty, "选择词典文件"))), "dictionary file picker");
            var filename = Find(picker, "1148");
            if (!filename.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
            {
                filename = filename.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                value = filename.GetCurrentPattern(ValuePattern.Pattern);
            }
            ((ValuePattern)value).SetValue(dictionary);
            var open = picker.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, "1"),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)));
            Invoke(open);
            WaitElement(() => Find(settings, "BrowseButton")?.Current.IsEnabled == true ? settings : null,
                "file picker closed");
            WaitElement(() => Find(settings, "DictionaryList")?.FindFirst(TreeScope.Descendants,
                new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                    new PropertyCondition(AutomationElement.NameProperty, Path.GetFileName(dictionary)))), "selected file appended");
            Check(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(configuration)).SequenceEqual(new[] { dictionary }),
                "selected dictionary is saved immediately without closing Settings");
            Check(File.ReadAllText(configuration).Contains("便携词典"), "published configuration displays Chinese directly");
            Check(Find(main, "StatusText").Current.Name.Contains("尚未设置词典"),
                "main window keeps its result while Settings is open");
            ((WindowPattern)settings.GetCurrentPattern(WindowPattern.Pattern)).Close();
            WaitElement(() => Find(main, "StatusText")?.Current.Name == "词典已打开。" ? main : null, "settings applied after close");
            var input = Find(main, "WordText");
            ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).SetValue("apple");
            input.SetFocus();
            PostMessage(process.MainWindowHandle, 0x0100, new IntPtr(13), IntPtr.Zero);
            PostMessage(process.MainWindowHandle, 0x0101, new IntPtr(13), IntPtr.Zero);
            WaitElement(() => Find(main, "StatusText")?.Current.Name.StartsWith("已加载：apple") == true ? main : null,
                "single-file dictionary rendering");
            Check(true, "bundled reader, WebView2 loader, embedded scripts and HTML display work");
            Check(!Directory.Exists(exe + ".WebView2") && !File.Exists(Path.Combine(workingDirectory, "dictionaries.json")),
                "cache and configuration do not follow the working directory");
            Stop(process);
            process.Dispose();
            process = null;

            var missingWebView = new ProcessStartInfo(exe) { WorkingDirectory = workingDirectory };
            missingWebView.Environment["WEBVIEW2_BROWSER_EXECUTABLE_FOLDER"] = Path.Combine(directory, "missing-browser");
            process = Process.Start(missingWebView);
            main = WaitElement(() => process.MainWindowHandle == IntPtr.Zero ? null :
                AutomationElement.FromHandle(process.MainWindowHandle), "missing WebView2 window");
            WaitElement(() => Find(main, "StatusText")?.Current.Name.Contains("安装 Microsoft Edge WebView2 Runtime") == true
                ? main : null, "WebView2 install prompt");
            Check(Find(main, "RuntimeHelp") != null, "published EXE explains missing WebView2 and provides download link");
            Stop(process);
            process.Dispose();
            process = null;

            // Give the native apphost a valid hostfxr but no shared frameworks.
            // The standard install prompt must appear before managed code can run.
            string runtimeRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location), "../../.."));
            string hostDirectory = Directory.GetDirectories(Path.Combine(runtimeRoot, "host", "fxr"))
                .OrderBy(path => Version.Parse(Path.GetFileName(path))).Last();
            string isolatedRoot = Path.Combine(directory, "empty-runtime");
            string isolatedHost = Path.Combine(isolatedRoot, "host", "fxr", Path.GetFileName(hostDirectory));
            Directory.CreateDirectory(isolatedHost);
            File.Copy(Path.Combine(hostDirectory, "hostfxr.dll"), Path.Combine(isolatedHost, "hostfxr.dll"));
            var missingDotnet = new ProcessStartInfo(exe) { WorkingDirectory = workingDirectory };
            missingDotnet.Environment["DOTNET_ROOT_X64"] = isolatedRoot;
            missingDotnet.Environment["DOTNET_ROOT"] = isolatedRoot;
            process = Process.Start(missingDotnet);
            var installPrompt = WaitElement(() => AutomationElement.RootElement.FindFirst(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id)), ".NET install prompt");
            var text = installPrompt.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
            string message = string.Join("\n", text.Cast<AutomationElement>().Select(item => item.Current.Name));
            Check(message.Contains(".NET") && Find(installPrompt, "WordText") == null,
                "EXE does not bundle .NET and displays the native runtime install prompt");
            Console.WriteLine("Published app: " + checks + " checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("PUBLISHED APP TEST FAILED: " + ex); return 1; }
        finally
        {
            if (process != null) { Stop(process); process.Dispose(); }
            Directory.Delete(directory, true);
        }
    }

    private static AutomationElement Find(AutomationElement window, string id) => window.FindFirst(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.AutomationIdProperty, id));

    private static AutomationElement SettingsWindow(int processId) => WaitElement(() =>
        AutomationElement.RootElement.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
            new PropertyCondition(AutomationElement.NameProperty, "词典设置"))), "Settings window");

    private static void Invoke(AutomationElement button) => ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    private static AutomationElement WaitElement(Func<AutomationElement> find, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            var element = find();
            if (element != null) return element;
            Thread.Sleep(50);
        }
        throw new TimeoutException(description);
    }

    private static void Stop(Process process)
    {
        if (process.HasExited) return;
        process.CloseMainWindow();
        if (!process.WaitForExit(5000)) { process.Kill(); process.WaitForExit(); }
    }

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new Exception(description);
        checks++;
        Console.WriteLine("PASS: " + description);
    }

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
