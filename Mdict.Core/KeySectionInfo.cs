using System;
using System.Collections.Generic;

namespace Mdict.Core
{
public class KeySectionInfo
{
    public long BlockCount { get; set; }
    public long EntryCount { get; set; }
    public long KeyInfoDecompressedSize { get; set; }
    public long KeyInfoCompressedSize { get; set; }
    public long KeyBlocksTotalSize { get; set; }
    public long KeyInfoFileOffset { get; set; }
    public long KeyBlocksFileOffset { get; set; }
    public long RecordSectionOffset { get; set; }
    public uint StoredPreambleChecksum { get; set; }
    public uint ComputedPreambleChecksum { get; set; }
    public uint StoredKeyInfoChecksum { get; set; }
    public uint ComputedKeyInfoChecksum { get; set; }
    public bool KeyInfoEncrypted { get; set; }
    public List<KeyBlockInfo> Blocks { get; set; } = new List<KeyBlockInfo>();
    public List<KeyEntry> FirstBlockEntries { get; set; } = new List<KeyEntry>();
    public int FirstBlockCompression { get; set; } = -1;
    public uint FirstBlockStoredChecksum { get; set; }
    public uint FirstBlockComputedChecksum { get; set; }

    // v0.3: the real Oxford key index. Each entry retains its original order.
    public List<KeyEntry> AllEntries { get; } = new List<KeyEntry>();
    public bool AllKeysLoaded { get; set; }
    public int ZlibBlockCount { get; set; }
    public int UncompressedBlockCount { get; set; }
    public int DuplicateExactKeyCount { get; private set; }

    // Keep only the position of the first exact key. Valid repeated headwords
    // are still preserved in AllEntries, including entries with equal offsets.
    private readonly Dictionary<string, int> firstPosition =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public void AddEntry(KeyEntry entry)
    {
        int index = AllEntries.Count;
        AllEntries.Add(entry);
        if (!firstPosition.TryAdd(entry.Key, index))
            DuplicateExactKeyCount++;
    }

    public KeyEntry FindExact(string word)
    {
        int index = FindExactIndex(word);
        return index < 0 ? null : AllEntries[index];
    }

    public int FindExactIndex(string word)
    {
        if (!AllKeysLoaded || word == null)
            return -1;
        int index;
        if (firstPosition.TryGetValue(word, out index))
            return index;
        return -1;
    }

    public List<KeyEntry> FindAllExact(string word, int limit)
    {
        List<KeyEntry> matches = new List<KeyEntry>();
        if (!AllKeysLoaded || string.IsNullOrEmpty(word) || limit <= 0)
            return matches;
        for (int i = 0; i < AllEntries.Count && matches.Count < limit; i++)
        {
            if (string.Equals(AllEntries[i].Key, word, StringComparison.OrdinalIgnoreCase))
                matches.Add(AllEntries[i]);
        }
        return matches;
    }

    public List<KeyEntry> FindPrefix(string prefix, int limit)
    {
        List<KeyEntry> matches = new List<KeyEntry>();
        if (!AllKeysLoaded || string.IsNullOrEmpty(prefix) || limit <= 0)
            return matches;
        for (int i = 0; i < AllEntries.Count && matches.Count < limit; i++)
        {
            if (AllEntries[i].Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                matches.Add(AllEntries[i]);
        }
        return matches;
    }
}
}
