using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Mdict.Core
{
// A small Oxford V14.3 reader: one MDX and exactly its matching MDD volumes.
// MDX and MDD indexes are loaded only when needed; resource bytes are read on demand.
public class OxfordDictionary
{
    public string MdxPath { get; private set; }
    public List<string> MddPaths { get; private set; } = new List<string>();

    private KeySectionInfo mdxKeys;
    private RecordSectionInfo mdxRecords;
    private readonly OxfordRecordReader recordReader = new OxfordRecordReader();
    private readonly Dictionary<string, OxfordMddReader> openedMdds =
        new Dictionary<string, OxfordMddReader>(StringComparer.OrdinalIgnoreCase);

    public OxfordDictionary(string mdxPath)
    {
        if (string.IsNullOrWhiteSpace(mdxPath) ||
            !mdxPath.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(mdxPath))
            throw new FileNotFoundException("Expected an existing Oxford .mdx file.", mdxPath);

        MdxPath = Path.GetFullPath(mdxPath);
        MddPaths = DiscoverMddVolumes(MdxPath);
    }

    // Only 'Name.mdd', 'Name.1.mdd', 'Name.2.mdd' etc. belong to Name.mdx.
    // Ignore unrelated MDDs in the same folder. No gaps are invented or required.
    public static List<string> DiscoverMddVolumes(string mdxPath)
    {
        string fullPath = Path.GetFullPath(mdxPath);
        string directory = Path.GetDirectoryName(fullPath);
        string stem = Path.GetFileNameWithoutExtension(fullPath);
        string firstName = stem + ".mdd";
        string prefix = stem + ".";
        string baseVolume = null;
        SortedDictionary<int, string> numbered = new SortedDictionary<int, string>();

        foreach (string path in Directory.GetFiles(directory))
        {
            string name = Path.GetFileName(path);
            if (name.Equals(firstName, StringComparison.OrdinalIgnoreCase))
            {
                if (baseVolume != null)
                    throw new InvalidDataException("Duplicate base MDD volume for " + stem);
                baseVolume = path;
                continue;
            }
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !name.EndsWith(".mdd", StringComparison.OrdinalIgnoreCase))
                continue;

            string number = name.Substring(prefix.Length,
                name.Length - prefix.Length - ".mdd".Length);
            int index;
            if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out index) ||
                index < 1 || number != index.ToString(CultureInfo.InvariantCulture))
                continue;
            if (numbered.ContainsKey(index))
                throw new InvalidDataException("Duplicate numbered MDD volume: " + index);
            numbered.Add(index, path);
        }

        List<string> paths = new List<string>();
        if (baseVolume != null)
            paths.Add(baseVolume);
        foreach (KeyValuePair<int, string> item in numbered)
            paths.Add(item.Value);
        return paths;
    }

    public string Lookup(string word)
    {
        LoadMdx();
        KeyEntry entry = mdxKeys.FindExact(word);
        if (entry == null)
            return null;
        int index = mdxKeys.AllEntries.IndexOf(entry);
        return recordReader.ReadHtml(MdxPath, mdxKeys, mdxRecords, index);
    }

    public List<string> SearchPrefix(string prefix, int limit)
    {
        LoadMdx();
        List<KeyEntry> matches = mdxKeys.FindPrefix(prefix, limit);
        List<string> result = new List<string>();
        foreach (KeyEntry entry in matches)
            result.Add(entry.Key);
        return result;
    }

    public List<OxfordResourceMatch> FindResources(string term, int limit)
    {
        List<OxfordResourceMatch> matches = new List<OxfordResourceMatch>();
        if (string.IsNullOrWhiteSpace(term) || limit <= 0)
            return matches;

        foreach (string path in MddPaths)
        {
            OxfordMddReader reader = OpenMdd(path);
            List<string> names = reader.FindResources(RemoveSoundScheme(term), limit - matches.Count);
            foreach (string name in names)
                matches.Add(new OxfordResourceMatch { Key = name, MddPath = path });
            if (matches.Count >= limit)
                break;
        }
        return matches;
    }

    // First try an exact resource path, then (only for a filename without
    // directories) accept one unique suffix match across all resource volumes.
    // This supports sound://apple__gb_5.mp3 even if the MDD uses a subfolder.
    public byte[] GetResource(string resourcePath, out OxfordResourceMatch match)
    {
        match = null;
        string requested = NormalizeResourcePath(RemoveSoundScheme(resourcePath));
        if (requested.Length == 0)
            return null;

        foreach (string volume in MddPaths)
        {
            OxfordMddReader reader = OpenMdd(volume);
            string actualKey = reader.FindExactKey(requested);
            if (actualKey != null)
            {
                match = new OxfordResourceMatch { Key = actualKey, MddPath = volume };
                return reader.GetResource(actualKey);
            }
        }

        // Only fallback for simple file names. Do not loosely match directory paths.
        if (requested.Contains('\\'))
            return null;
        string ending = "\\" + requested;
        OxfordResourceMatch candidate = null;
        foreach (string volume in MddPaths)
        {
            OxfordMddReader reader = OpenMdd(volume);
            foreach (KeyEntry entry in reader.Keys.AllEntries)
            {
                string actual = NormalizeResourcePath(entry.Key);
                if (actual.EndsWith(ending, StringComparison.OrdinalIgnoreCase))
                {
                    if (candidate != null)
                        throw new InvalidOperationException("Ambiguous resource filename '" + requested +
                            "'. Use --oxford-find and pass its full MDD key to --oxford-get.");
                    candidate = new OxfordResourceMatch { Key = entry.Key, MddPath = volume };
                }
            }
        }
        if (candidate == null)
            return null;
        match = candidate;
        return OpenMdd(candidate.MddPath).GetResource(candidate.Key);
    }

    private OxfordMddReader OpenMdd(string path)
    {
        OxfordMddReader reader;
        if (!openedMdds.TryGetValue(path, out reader))
        {
            reader = new OxfordMddReader(path);
            openedMdds.Add(path, reader);
        }
        return reader;
    }

    private void LoadMdx()
    {
        if (mdxKeys != null)
            return;
        MdictHeader header = new OxfordHeaderReader().Read(MdxPath);
        KeySectionInfo keys = new OxfordKeyReader().Read(header, true);
        RecordSectionInfo records = recordReader.ReadIndex(header, keys);
        // Publish only a complete index so a failed load can be retried.
        mdxRecords = records;
        mdxKeys = keys;
    }

    private static string NormalizeResourcePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        return path.Replace('/', '\\').TrimStart('\\');
    }

    private static string RemoveSoundScheme(string text)
    {
        if (text == null)
            return "";
        if (text.StartsWith("sound://", StringComparison.OrdinalIgnoreCase))
            return text.Substring("sound://".Length);
        return text;
    }
}
}
