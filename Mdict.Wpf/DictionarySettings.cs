using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Mdict.Wpf
{
    public static class DictionarySettings
    {
        public const int MaxDictionaries = 8;
        public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "dictionaries.json");

        public static void Initialize(string path)
        {
            if (File.Exists(path)) return;
            // CreateNew never overwrites a configuration created by another instance.
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
                writer.Write("[]");
        }

        public static List<string> Load(string path)
        {
            if (!File.Exists(path)) return new List<string>();
            var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
            if (paths == null || paths.Count > MaxDictionaries || paths.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("词典设置文件格式不正确。");
            // Missing dictionaries remain listed so they can be removed in Settings.
            try { return ValidatePaths(paths, false); }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException("词典设置文件格式不正确：" + ex.Message, ex);
            }
        }

        public static List<string> ValidatePaths(IEnumerable<string> paths, bool requireExistingFiles = true)
        {
            var result = paths.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => Path.GetFullPath(p.Trim())).ToList();
            if (result.Count > MaxDictionaries)
                throw new ArgumentException("最多可以设置 8 个词典路径。");
            if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
                throw new ArgumentException("词典路径不能重复。");
            foreach (string path in result)
            {
                if (!string.Equals(Path.GetExtension(path), ".mdx", StringComparison.OrdinalIgnoreCase) ||
                    (requireExistingFiles && !File.Exists(path)))
                    throw new ArgumentException("请选择存在的 MDX 文件：" + path);
            }
            return result;
        }

        public static void Save(string path, IEnumerable<string> paths)
        {
            // Retain missing entries so users can reorder or remove them; new selections are checked by the dialog.
            var validated = ValidatePaths(paths, false);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(validated,
                    new JsonSerializerOptions { WriteIndented = true,
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) }), new UTF8Encoding(false));
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
