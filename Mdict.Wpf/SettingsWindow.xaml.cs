using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Mdict.Wpf
{
    public partial class SettingsWindow : Window
    {
        private readonly string settingsPath;
        private readonly DispatcherTimer errorTimer = new DispatcherTimer();
        public IReadOnlyList<string> ResultPaths { get; private set; }
        public bool HasChanges { get; private set; }

        public SettingsWindow(IEnumerable<string> paths, string configurationPath)
        {
            settingsPath = configurationPath;
            ResultPaths = paths.ToList();
            InitializeComponent();
            errorTimer.Interval = TimeSpan.FromSeconds(6);
            errorTimer.Tick += (_, _) => ShowError("");
            Closed += (_, _) => errorTimer.Stop();
            WindowSizeLimits.Attach(this, 500, 500);
            foreach (string path in ResultPaths)
                DictionaryList.Items.Add(new ListBoxItem { Content = Path.GetFileName(path), Tag = path });
            UpdateButtons();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            // Disable the maximize button without disabling edge resizing.
            SetWindowLong(handle, -16, GetWindowLong(handle, -16) & ~0x00010000);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr handle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr handle, int index, int value);

        private IEnumerable<string> CurrentPaths() => DictionaryList.Items.Cast<ListBoxItem>().Select(item => (string)item.Tag);

        private void ShowError(string text)
        {
            errorTimer.Stop();
            ErrorText.Text = text;
            if (text.Length > 0) errorTimer.Start();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!BrowseButton.IsEnabled) return;
            var dialog = new OpenFileDialog { Title = "选择词典文件", Filter = "MDX 词典 (*.mdx)|*.mdx", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) AddDictionary(dialog.FileName);
        }

        private void AddDictionary(string path)
        {
            try
            {
                path = Path.GetFullPath(path);
                if (CurrentPaths().Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    ShowError("该词典已在列表中。");
                    return;
                }
                DictionarySettings.ValidatePaths(new[] { path });
                var paths = CurrentPaths().ToList();
                paths.Add(path);
                ApplyChange(paths, paths.Count - 1);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is NotSupportedException)
            {
                ShowError(ex.Message);
            }
        }

        private void DictionaryList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

        private void UpdateButtons()
        {
            if (BrowseButton == null) return;
            int selected = DictionaryList.SelectedIndex;
            BrowseButton.IsEnabled = DictionaryList.Items.Count < DictionarySettings.MaxDictionaries;
            MoveUpButton.IsEnabled = selected > 0;
            MoveDownButton.IsEnabled = selected >= 0 && selected < DictionaryList.Items.Count - 1;
            RemoveButton.IsEnabled = selected >= 0;
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
        private void MoveDownButton_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

        private void MoveSelected(int direction)
        {
            int selected = DictionaryList.SelectedIndex;
            int target = selected + direction;
            if (selected < 0 || target < 0 || target >= DictionaryList.Items.Count) return;
            var paths = CurrentPaths().ToList();
            string path = paths[selected];
            paths.RemoveAt(selected);
            paths.Insert(target, path);
            ApplyChange(paths, target);
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            int selected = DictionaryList.SelectedIndex;
            if (selected < 0) return;
            var paths = CurrentPaths().ToList();
            paths.RemoveAt(selected);
            ApplyChange(paths, Math.Min(selected, paths.Count - 1));
        }

        private void ApplyChange(List<string> paths, int selected)
        {
            try
            {
                DictionarySettings.Save(settingsPath, paths);
                ResultPaths = paths;
                HasChanges = true;
                DictionaryList.Items.Clear();
                foreach (string path in paths)
                    DictionaryList.Items.Add(new ListBoxItem { Content = Path.GetFileName(path), Tag = path });
                DictionaryList.SelectedIndex = selected;
                if (selected >= 0) DictionaryList.ScrollIntoView(DictionaryList.SelectedItem);
                ShowError("");
                UpdateButtons();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException ||
                ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                ShowError("设置保存失败：" + ex.Message);
            }
        }

        private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        }
    }
}
