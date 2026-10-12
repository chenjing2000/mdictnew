using System;
using System.Collections.Generic;
using System.IO;
using Mdict.Core;

namespace Mdict.Application
{
    // Business entry point. The WPF project never directly reads MDX/MDD.
    public class DictionaryService
    {
        private OxfordDictionary dictionary;
        // Reader indexes are lazily loaded and not thread-safe. One lock for all operations.
        private readonly object sync = new object();

        public bool IsOpen
        {
            get { lock (sync) { return dictionary != null; } }
        }

        public void Open(string mdxPath)
        {
            OxfordDictionary next = new OxfordDictionary(mdxPath);
            lock (sync) { dictionary = next; }
        }

        public string Lookup(string word)
        {
            lock (sync)
            {
                if (dictionary == null)
                    throw new InvalidOperationException("No dictionary is open.");
                if (string.IsNullOrWhiteSpace(word))
                    return null;
                return dictionary.Lookup(word.Trim());
            }
        }

        public List<string> SearchPrefix(string prefix, int limit)
        {
            lock (sync)
            {
                if (dictionary == null)
                    throw new InvalidOperationException("No dictionary is open.");
                return dictionary.SearchPrefix(prefix, limit);
            }
        }

        public byte[] GetResource(string path)
        {
            lock (sync)
            {
                if (dictionary == null)
                    throw new InvalidOperationException("No dictionary is open.");
                OxfordResourceMatch match;
                byte[] content = dictionary.GetResource(path, out match);
                if (content != null) return content;
                return ReadLocalResource(path);
            }
        }

        private byte[] ReadLocalResource(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (path.StartsWith("sound://", StringComparison.OrdinalIgnoreCase))
                path = path.Substring("sound://".Length);
            string[] segments = path.Replace('\\', '/').TrimStart('/').Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment.EndsWith('.') || segment.EndsWith(' ') ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    return null;
            }

            string directory = Path.GetDirectoryName(dictionary.MdxPath);
            string localPath = directory;
            foreach (string segment in segments) localPath = Path.Combine(localPath, segment);
            if (!File.Exists(localPath)) return null;

            // Do not follow a resource file or subdirectory that redirects outside the dictionary.
            string current = directory;
            foreach (string segment in segments)
            {
                current = Path.Combine(current, segment);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
            }
            return File.ReadAllBytes(localPath);
        }

        public void Close()
        {
            lock (sync) { dictionary = null; }
        }

    }
}
