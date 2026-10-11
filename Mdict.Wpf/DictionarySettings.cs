using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Mdict.Wpf
{
    public static class DictionarySettings
    {
        public const int MaxDictionaries = 8;
        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MdictViewer", "dictionaries.json");

        public static List<string> Load(string path)
        {
            if (!File.Exists(path)) return new List<string>();
            var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
            if (paths == null || paths.Count > MaxDictionaries || paths.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("词典设置文件格式不正确。");
            // Missing dictionaries remain visible and editable in Settings.
            return paths;
        }

        public static List<string> ValidatePaths(IEnumerable<string> paths)
        {
            var result = paths.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => Path.GetFullPath(p.Trim())).ToList();
            if (result.Count > MaxDictionaries)
                throw new ArgumentException("最多可以设置 8 个词典路径。");
            if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
                throw new ArgumentException("词典路径不能重复。");
            foreach (string path in result)
            {
                if (!string.Equals(Path.GetExtension(path), ".mdx", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    throw new ArgumentException("请选择存在的 MDX 文件：" + path);
            }
            return result;
        }

        public static void Save(string path, IEnumerable<string> paths)
        {
            var validated = ValidatePaths(paths);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(validated,
                    new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
