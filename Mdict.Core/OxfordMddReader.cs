using System;
using System.Collections.Generic;
using System.IO;

namespace Mdict.Core
{
// Read one Oxford MDD volume. Open the index once; read resource bytes on demand.
// Pass a particular .mdd (or .1.mdd) file; no bulk extraction or HTML transformation.
public class OxfordMddReader
{
    public MdictHeader Header { get; private set; }
    public KeySectionInfo Keys { get; private set; }
    public RecordSectionInfo Records { get; private set; }

    private readonly OxfordRecordReader recordReader = new OxfordRecordReader();
    private readonly Dictionary<string, int> positions =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public OxfordMddReader(string path)
    {
        Header = new OxfordHeaderReader().Read(path);
        if (!Header.FilePath.EndsWith(".mdd", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected an MDD file.");
        Keys = new OxfordKeyReader().Read(Header, true);
        Records = recordReader.ReadIndex(Header, Keys);
        for (int i = 0; i < Keys.AllEntries.Count; i++)
        {
            string key = Normalize(Keys.AllEntries[i].Key);
            if (!positions.ContainsKey(key))
                positions.Add(key, i);
        }
    }

    public string FindExactKey(string path)
    {
        int index;
        if (positions.TryGetValue(Normalize(path), out index))
            return Keys.AllEntries[index].Key;
        return null;
    }

    public byte[] GetResource(string path)
    {
        int index;
        if (!positions.TryGetValue(Normalize(path), out index))
            return null;
        return recordReader.ReadRawRecord(Header.FilePath, Keys, Records, index);
    }

    public List<string> FindResources(string term, int limit)
    {
        List<string> results = new List<string>();
        if (string.IsNullOrEmpty(term) || limit <= 0)
            return results;
        string needle = Normalize(term);
        for (int i = 0; i < Keys.AllEntries.Count && results.Count < limit; i++)
        {
            string key = Keys.AllEntries[i].Key;
            if (Normalize(key).Contains(needle, StringComparison.OrdinalIgnoreCase))
                results.Add(key);
        }
        return results;
    }

    // Normalize only path separators and a leading slash. Never transform filename spelling.
    private static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        return path.Replace('/', '\\').TrimStart('\\');
    }
}
}
