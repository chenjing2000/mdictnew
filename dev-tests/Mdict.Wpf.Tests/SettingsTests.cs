using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Mdict.Wpf;

internal static class SettingsTests
{
    private static int checks;
    private static int failures;

    public static int Run()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string directory = Path.Combine(Path.GetTempPath(), "MdictViewer-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        SettingsWindow dialog = null;
        try
        {
            string a = CreateFile(directory, "中文词典");
            string b = CreateFile(directory, "Second");
            string configuration = Path.Combine(directory, "settings.json");
            foreach (string[] invalid in new[] { new[] { a, a.ToUpperInvariant() }, new[] { Path.ChangeExtension(a, ".txt") } })
            {
                File.WriteAllText(configuration, System.Text.Json.JsonSerializer.Serialize(invalid));
                bool rejected = false;
                try { DictionarySettings.Load(configuration); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "loading rejects duplicate or non-MDX configuration paths");
            }
            DictionarySettings.Save(configuration, new[] { a, b });
            string json = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(configuration));
            Check(json.Contains("中文词典") && !json.Contains("\\u") && DictionarySettings.Load(configuration).SequenceEqual(new[] { a, b }),
                "UTF-8 JSON displays Chinese and preserves ordered paths");
            dialog = new SettingsWindow(new[] { a, b }, configuration);
            Check(dialog.Width == 450 && dialog.Height == 320 && dialog.MinWidth == 420 && dialog.MinHeight == 300 &&
                dialog.MaxWidth == 500 && dialog.MaxHeight == 500, "settings default and size limits match the compact window");
            var list = (ListBox)dialog.FindName("DictionaryList");
            var error = (TextBlock)dialog.FindName("ErrorText");
            Check(error.Visibility == Visibility.Collapsed && Grid.GetRow(error) >
                Grid.GetRow((UIElement)((Button)dialog.FindName("BrowseButton")).Parent),
                "empty error row is hidden and remains below the settings controls");
            Check(dialog.FontSize == 14 && dialog.FindName("SaveButton") == null && dialog.FindName("CancelButton") == null &&
                ((Button)dialog.FindName("BrowseButton")).Content is Image,
                "settings uses size 14 and four icon buttons without save or cancel");
            Check(list.SelectionMode == SelectionMode.Single && list.Items.Cast<ListBoxItem>()
                .Select(item => (string)item.Content).SequenceEqual(new[] { Path.GetFileName(a), Path.GetFileName(b) }),
                "list displays only dictionary filenames");
            Check(!Button(dialog, "MoveUpButton").IsEnabled && !Button(dialog, "MoveDownButton").IsEnabled &&
                !Button(dialog, "RemoveButton").IsEnabled, "unselected list disables ordering and deletion");
            list.SelectedIndex = 0;
            Check(!Button(dialog, "MoveUpButton").IsEnabled && Button(dialog, "MoveDownButton").IsEnabled,
                "first dictionary can only move down");
            Click(dialog, "MoveDownButton");
            Check(DictionarySettings.Load(configuration).SequenceEqual(new[] { b, a }) && list.SelectedIndex == 1,
                "move down saves immediately and retains selection");
            Check(Button(dialog, "MoveUpButton").IsEnabled && !Button(dialog, "MoveDownButton").IsEnabled,
                "last dictionary can only move up");
            Click(dialog, "MoveUpButton");
            Check(DictionarySettings.Load(configuration).SequenceEqual(new[] { a, b }) && list.SelectedIndex == 0,
                "move up saves immediately and retains selection");
            Click(dialog, "RemoveButton");
            Check(DictionarySettings.Load(configuration).SequenceEqual(new[] { b }) && list.SelectedIndex == 0,
                "deletion saves immediately and selects the next dictionary");
            Add(dialog, a);
            Check(DictionarySettings.Load(configuration).SequenceEqual(new[] { b, a }) && list.SelectedIndex == 1,
                "file selection appends and saves immediately");
            Add(dialog, a.ToUpperInvariant());
            Check(error.Visibility == Visibility.Visible, "failed operation reveals the error row");
            Check(list.Items.Count == 2 && ((TextBlock)dialog.FindName("ErrorText")).Text.Contains("已在列表中") &&
                DictionarySettings.Load(configuration).SequenceEqual(new[] { b, a }), "duplicate selection does not change configuration");
            Add(dialog, Path.Combine(directory, "missing.mdx"));
            Check(list.Items.Count == 2, "missing new dictionary is rejected");
            string other = Path.Combine(directory, "other");
            Directory.CreateDirectory(other);
            Add(dialog, CreateFile(other, "中文词典"));
            Check(error.Visibility == Visibility.Collapsed, "successful operation hides the error row again");
            Check(list.Items.Count == 3, "same filename from another directory is allowed");
            for (int i = 3; i < 8; i++) Add(dialog, CreateFile(directory, "Copy" + i));
            Check(list.Items.Count == 8 && !Button(dialog, "BrowseButton").IsEnabled, "eight dictionaries disable file selection");
            Add(dialog, CreateFile(directory, "Ninth"));
            Check(list.Items.Count == 8 && DictionarySettings.Load(configuration).Count == 8, "ninth selection leaves configuration unchanged");
            while (list.Items.Count > 0) Click(dialog, "RemoveButton");
            Check(DictionarySettings.Load(configuration).Count == 0 && list.SelectedIndex == -1 && Button(dialog, "BrowseButton").IsEnabled,
                "deleting all dictionaries saves an empty list");
            dialog.Close();
            Check(DictionarySettings.Load(configuration).Count == 0, "closing never rolls back successful changes");
            DictionarySettings.Save(configuration, new[] { a, b });
            dialog = new SettingsWindow(new[] { a, b }, configuration);
            list = (ListBox)dialog.FindName("DictionaryList");
            using (var locked = new FileStream(configuration, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                list.SelectedIndex = 0;
                Click(dialog, "MoveDownButton");
                list.SelectedIndex = 1;
                Click(dialog, "MoveUpButton");
                Click(dialog, "RemoveButton");
                Add(dialog, CreateFile(directory, "Blocked"));
                Check(list.Items.Cast<ListBoxItem>().Select(item => (string)item.Tag).SequenceEqual(new[] { a, b }) &&
                    !dialog.HasChanges && DictionarySettings.Load(configuration).SequenceEqual(new[] { a, b }) &&
                    ((TextBlock)dialog.FindName("ErrorText")).Text.StartsWith("设置保存失败："),
                    "failed writes from all four operations preserve list configuration and change flag");
            }
            File.Delete(a);
            Add(dialog, CreateFile(directory, "Recovery"));
            Check(DictionarySettings.Load(configuration).Count == 3, "missing old file does not block adding a valid dictionary");
            list.SelectedIndex = 0;
            Click(dialog, "MoveDownButton");
            Click(dialog, "RemoveButton");
            Check(DictionarySettings.Load(configuration).Count == 2 && !DictionarySettings.Load(configuration).Contains(a),
                "missing old file can be reordered and removed");
        }
        finally { dialog?.Close(); Directory.Delete(directory, true); app.Shutdown(); }
        Console.WriteLine($"Settings: {checks} checks; {failures} failures.");
        return failures == 0 ? 0 : 1;
    }

    private static string CreateFile(string directory, string name)
    {
        string path = Path.Combine(directory, name + ".mdx");
        File.WriteAllText(path, "");
        return path;
    }

    internal static void Add(SettingsWindow dialog, string path) => typeof(SettingsWindow)
        .GetMethod("AddDictionary", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dialog, new object[] { path });
    private static Button Button(SettingsWindow dialog, string name) => (Button)dialog.FindName(name);
    private static void Click(SettingsWindow dialog, string name) => Button(dialog, name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static void Check(bool passed, string message)
    {
        checks++;
        if (!passed) failures++;
        Console.WriteLine((passed ? "PASS: " : "FAIL: ") + message);
    }
}
