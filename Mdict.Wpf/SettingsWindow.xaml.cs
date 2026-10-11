using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Mdict.Wpf
{
    public partial class SettingsWindow : Window
    {
        private readonly List<TextBox> pathInputs = new List<TextBox>();
        public IReadOnlyList<string> ResultPaths { get; private set; }

        public SettingsWindow(IEnumerable<string> paths)
        {
            InitializeComponent();
            WindowSizeLimits.Attach(this, 1000, 700);
            foreach (string path in paths) AddRow(path);
            if (pathInputs.Count == 0) AddRow("");
            UpdateAddButton();
        }

        private void AddRow(string path)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var input = new TextBox { Text = path, Style = (Style)FindResource("PathInput") };
            input.TextChanged += (_, _) => { ErrorText.Text = ""; UpdateAddButton(); };
            pathInputs.Add(input);
            row.Children.Add(input);
            var browse = IconButton("BrowseIcon", "选择 MDX 文件");
            Grid.SetColumn(browse, 1);
            browse.Click += (_, _) =>
            {
                var dialog = new OpenFileDialog { Title = "选择 MDX 词典", Filter = "MDX 词典 (*.mdx)|*.mdx", CheckFileExists = true };
                if (dialog.ShowDialog(this) == true) input.Text = dialog.FileName;
            };
            row.Children.Add(browse);
            var remove = IconButton("RemoveIcon", "删除此词典路径");
            Grid.SetColumn(remove, 2);
            remove.Click += (_, _) =>
            {
                pathInputs.Remove(input);
                PathRows.Children.Remove(row);
                if (pathInputs.Count == 0) AddRow("");
                UpdateAddButton();
            };
            row.Children.Add(remove);
            PathRows.Children.Add(row);
            UpdateAddButton();
        }

        private Button IconButton(string imageKey, string label)
        {
            var button = new Button { Content = new Image { Source = (System.Windows.Media.ImageSource)FindResource(imageKey),
                Width = 24, Height = 24 }, Style = (Style)FindResource("SettingsIconButton"),
                Margin = new Thickness(8, 0, 0, 0), ToolTip = label };
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            return button;
        }

        private void UpdateAddButton()
        {
            if (AddButton == null) return;
            string last = pathInputs.LastOrDefault()?.Text.Trim() ?? "";
            bool valid = File.Exists(last) && string.Equals(Path.GetExtension(last), ".mdx", StringComparison.OrdinalIgnoreCase);
            AddButton.Visibility = pathInputs.Count < DictionarySettings.MaxDictionaries && valid
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddButton.Visibility != Visibility.Visible || pathInputs.Count >= DictionarySettings.MaxDictionaries) return;
            AddRow("");
            pathInputs.Last().Focus();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ResultPaths = DictionarySettings.ValidatePaths(pathInputs.Select(p => p.Text));
                DialogResult = true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is NotSupportedException)
            {
                ErrorText.Text = ex.Message;
            }
        }
    }
}
