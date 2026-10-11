using System;
using System.Collections.Generic;
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
                return dictionary.GetResource(path, out match);
            }
        }

        public void Close()
        {
            lock (sync) { dictionary = null; }
        }

    }
}
