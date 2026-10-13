using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Mdict.Wpf;

internal static class NotificationTests
{
    public static int Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "MdictViewer-notifications-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var app = new Application();
        var window = new MainWindow(Path.Combine(directory, "dictionaries.json"));
        int checks = 0;
        int exitCode = 0;
        var clipboard = Clipboard.GetDataObject();
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                await Ready(window);
                Silent("normal startup");
                string dictionary = DictionaryFixture.Create(directory, "中文词典", "green", "only-test");
                await (Task)Invoke("ApplyDictionaryPathsAsync", (object)new[] { dictionary });
                Silent("successful dictionary opening");
                await Lookup("apple");
                Silent("successful lookup");
                Invoke("OnTextSelectionRequested", "selected text", false);
                Check(Clipboard.GetText() == "selected text", "selection still copies");
                Silent("successful copy");
                await Lookup("absent-word");
                Silent("missing entry");
                await Lookup("");
                Silent("empty query");
                var sections = Field<List<DictionaryResultView>>(window, "sections");
                sections[0].Controller.Dispose();
                await Lookup("banana");
                var text = ((TextBlock)window.FindName("StatusText")).Text;
                Check(text.StartsWith("Error: ") && text.All(c => c < 128), "display failure is an English error even with a Chinese filename");
                Check(((UIElement)window.FindName("StatusPanel")).Visibility == Visibility.Visible, "error shows the information bar");
                string malformed = Path.Combine(directory, "malformed.mdx");
                File.WriteAllText(malformed, "not a dictionary");
                await (Task)Invoke("ApplyDictionaryPathsAsync", (object)new[]
                {
                    Path.Combine(directory, "missing.mdx"), malformed, dictionary
                });
                var input = (TextBox)window.FindName("WordText");
                input.Focus();
                input.Text = "ap";
                Invoke("SuggestionTimer_Tick", null, EventArgs.Empty);
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (Field<bool>(window, "suggestionSearchRunning"))
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("Suggestion warning");
                    await Task.Delay(25);
                }
                text = ((TextBlock)window.FindName("StatusText")).Text;
                Check(text.Contains("Dictionary #2:") && !text.Contains("Dictionary #1:"),
                    "suggestion failures identify the configured dictionary after an unavailable entry");
                string oldFolder = Environment.GetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER");
                var missingRuntime = new MainWindow(Path.Combine(directory, "missing-runtime.json"));
                try
                {
                    Environment.SetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", Path.Combine(directory, "no-browser"));
                    missingRuntime.Show();
                    await Ready(missingRuntime);
                    text = ((TextBlock)missingRuntime.FindName("StatusText")).Text;
                    Check(text.StartsWith("Warning: Install Microsoft Edge WebView2 Runtime") && text.All(c => c < 128), "missing runtime shows an English warning");
                }
                finally
                {
                    missingRuntime.Close();
                    Environment.SetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", oldFolder);
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exitCode = 1; }
            finally
            {
                if (clipboard != null) Clipboard.SetDataObject(clipboard, true);
                window.Close();
                app.Shutdown();
            }
        };
        try { app.Run(window); }
        finally
        {
            Directory.Delete(directory, true);
        }
        Console.WriteLine($"Notifications: {checks} checks; exit code {exitCode}.");
        return exitCode;

        object Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, args);
        Task Lookup(string word) => (Task)Invoke("LookupAsync", word, true, -1, true);
        void Silent(string operation)
        {
            Check(((TextBlock)window.FindName("StatusText")).Text == "" &&
                ((UIElement)window.FindName("StatusPanel")).Visibility == Visibility.Collapsed, operation + " is silent and collapses the bar");
        }
        void Check(bool passed, string description)
        {
            checks++;
            if (!passed) throw new InvalidOperationException(description);
            Console.WriteLine("PASS: " + description);
        }
    }

    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
    private static async Task Ready(MainWindow window)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!Field<bool>(window, "browserReady") || Field<bool>(window, "busy"))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Window startup");
            await Task.Delay(25);
        }
    }
}
