using Mdict.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MdictReaderTest
{
internal class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 1 && args[0] == "--self-test")
            return SelfTest.Run();

        bool oxfordInfo = args.Length == 2 && args[0] == "--oxford-info";
        bool oxfordFind = (args.Length == 3 || args.Length == 4) && args[0] == "--oxford-find";
        bool oxfordGet = args.Length == 4 && args[0] == "--oxford-get";
        bool oxfordLookup = (args.Length == 3 || args.Length == 4) && args[0] == "--oxford-lookup";
        if (oxfordInfo || oxfordFind || oxfordGet || oxfordLookup)
        {
            try { return RunOxford(args, oxfordInfo, oxfordFind, oxfordGet, oxfordLookup); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
                return 2;
            }
        }
        bool mddInfo = args.Length == 2 && args[0] == "--mdd-info";
        bool mddFind = (args.Length == 3 || args.Length == 4) && args[0] == "--mdd-find";
        bool mddGet = args.Length == 4 && args[0] == "--mdd-get";
        if (mddInfo || mddFind || mddGet)
        {
            try { return RunMdd(args, mddInfo, mddFind, mddGet); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
                return 2;
            }
        }
        bool headerOnly = args.Length == 2 && args[0] == "--header-only";
        bool firstOnly = args.Length == 2 && args[0] == "--first-block";
        bool find = args.Length == 3 && args[0] == "--find";
        bool prefix = (args.Length == 3 || args.Length == 4) && args[0] == "--prefix";
        bool lookup = (args.Length == 3 || args.Length == 4) && args[0] == "--lookup";
        if (args.Length != 1 && !headerOnly && !firstOnly && !find && !prefix && !lookup)
        {
            Console.WriteLine("MdictReaderTest v0.6 - Oxford MDX / MDD reading");
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run -- \"D:\\Dictionaries\\Oxford.mdx\"");
            Console.WriteLine("  dotnet run -- --header-only \"D:\\Dictionaries\\Oxford.mdx\"");
            Console.WriteLine("  dotnet run -- --first-block \"D:\\Dictionaries\\Oxford.mdx\"");
            Console.WriteLine("  dotnet run -- --find \"D:\\Dictionaries\\Oxford.mdx\" apple");
            Console.WriteLine("  dotnet run -- --prefix \"D:\\Dictionaries\\Oxford.mdx\" appl 20");
            Console.WriteLine("  dotnet run -- --lookup \"D:\\Dictionaries\\Oxford.mdx\" apple apple.html");
            Console.WriteLine("  dotnet run -- --mdd-info \"D:\\Dictionaries\\Oxford.mdd\"");
            Console.WriteLine("  dotnet run -- --mdd-find \"D:\\Dictionaries\\Oxford.mdd\" apple 20");
            Console.WriteLine("  dotnet run -- --mdd-get \"D:\\Dictionaries\\Oxford.mdd\" \"\\apple__gb_5.mp3\" apple.mp3");
            Console.WriteLine("  dotnet run -- --oxford-info \"D:\\Dictionaries\\Oxford.mdx\"");
            Console.WriteLine("  dotnet run -- --oxford-find \"D:\\Dictionaries\\Oxford.mdx\" apple__gb_5.mp3");
            Console.WriteLine("  dotnet run -- --oxford-get \"D:\\Dictionaries\\Oxford.mdx\" sound://apple__gb_5.mp3 apple.mp3");
            Console.WriteLine("  dotnet run -- --oxford-lookup \"D:\\Dictionaries\\Oxford.mdx\" apple apple.html");
            Console.WriteLine("  dotnet run -- --self-test");
            return 1;
        }

        string path = find || prefix || lookup ? args[1] : args[args.Length - 1];
        // A .mdd path by itself shows its resource index, like dragging it onto run.cmd.
        if (args.Length == 1 && path.EndsWith(".mdd", StringComparison.OrdinalIgnoreCase))
        {
            try { return RunMdd(new string[] { "--mdd-info", path }, true, false, false); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
                return 2;
            }
        }
        try
        {
            OxfordHeaderReader headerReader = new OxfordHeaderReader();
            MdictHeader header = headerReader.Read(path);
            PrintHeader(header);
            if (!headerOnly && Path.GetExtension(path).Equals(".mdx", StringComparison.OrdinalIgnoreCase))
            {
                OxfordKeyReader keyReader = new OxfordKeyReader();
                KeySectionInfo section = keyReader.Read(header, !firstOnly);
                PrintKeySection(section);
                if (section.AllKeysLoaded)
                {
                    PrintFullIndex(section);
                    if (find)
                        PrintExactMatches(section, args[2]);
                    if (prefix)
                    {
                        int limit = 20;
                        if (args.Length == 4 &&
                            (!int.TryParse(args[3], out limit) || limit < 1 || limit > 100))
                            throw new ArgumentException("Prefix limit must be from 1 to 100.");
                        PrintPrefixMatches(section, args[2], limit);
                    }
                    // Preserve v0.3 --find/--prefix behavior: these need Key data only.
                    if (!find && !prefix)
                    {
                        OxfordRecordReader recordReader = new OxfordRecordReader();
                        RecordSectionInfo records = recordReader.ReadIndex(header, section);
                        PrintRecordInfo(records);
                        if (lookup)
                        {
                            string outputPath = args.Length == 4 ? args[3] : null;
                            PrintHtmlLookup(recordReader, header, section, records, args[2], outputPath);
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("Key reading skipped (--header-only selected).");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.GetType().Name + ": " + ex.Message);
            return 2;
        }
    }

    private static int RunOxford(string[] args, bool info, bool find, bool get, bool lookup)
    {
        OxfordDictionary dictionary = new OxfordDictionary(args[1]);
        Console.WriteLine("MdictReaderTest v0.6");
        Console.WriteLine("[Oxford V14.3]");
        Console.WriteLine("MDX                  : " + dictionary.MdxPath);
        Console.WriteLine("Matching MDD volumes : " + dictionary.MddPaths.Count);
        for (int i = 0; i < dictionary.MddPaths.Count; i++)
            Console.WriteLine("  [" + (i + 1) + "] " + Path.GetFileName(dictionary.MddPaths[i]));

        if (info)
            return 0;

        if (find)
        {
            int limit = 20;
            if (args.Length == 4 &&
                (!int.TryParse(args[3], out limit) || limit < 1 || limit > 100))
                throw new ArgumentException("Resource match limit must be 1..100.");
            List<OxfordResourceMatch> found = dictionary.FindResources(args[2], limit);
            Console.WriteLine();
            Console.WriteLine("[Resource matches: " + args[2] + "]");
            Console.WriteLine("Matches (max " + limit + ") : " + found.Count);
            for (int i = 0; i < found.Count; i++)
                Console.WriteLine("  " + found[i].Key + "  [" + Path.GetFileName(found[i].MddPath) + "]");
            return 0;
        }

        if (get)
        {
            OxfordResourceMatch match;
            byte[] data = dictionary.GetResource(args[2], out match);
            if (data == null)
            {
                Console.WriteLine("Resource NOT FOUND: " + args[2]);
                return 3;
            }
            Console.WriteLine();
            Console.WriteLine("[Resource extraction]");
            Console.WriteLine("Matched key          : " + match.Key);
            Console.WriteLine("Found in MDD         : " + Path.GetFileName(match.MddPath));
            Console.WriteLine("Bytes                : " + data.Length);
            Console.WriteLine("SHA-256              : " +
                Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
            File.WriteAllBytes(args[3], data);
            Console.WriteLine("Saved to             : " + Path.GetFullPath(args[3]));
            return 0;
        }

        if (lookup)
        {
            string html = dictionary.Lookup(args[2]);
            if (html == null)
            {
                Console.WriteLine("Word NOT FOUND: " + args[2]);
                return 3;
            }
            byte[] raw = Encoding.UTF8.GetBytes(html);
            Console.WriteLine();
            Console.WriteLine("[HTML lookup: " + args[2] + "]");
            Console.WriteLine("HTML UTF-8 bytes     : " + raw.Length);
            Console.WriteLine("HTML SHA-256         : " +
                Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant());
            Console.WriteLine("HTML preview         : " +
                html.Substring(0, Math.Min(250, html.Length)).Replace("\r", "\\r").Replace("\n", "\\n"));
            if (args.Length == 4)
            {
                File.WriteAllBytes(args[3], raw);
                Console.WriteLine("Saved to             : " + Path.GetFullPath(args[3]));
            }
        }
        return 0;
    }

    private static int RunMdd(string[] args, bool info, bool find, bool get)
    {
        OxfordMddReader reader = new OxfordMddReader(args[1]);
        Console.WriteLine("MdictReaderTest v0.6");
        Console.WriteLine("[MDD]");
        Console.WriteLine("Path                 : " + reader.Header.FilePath);
        Console.WriteLine("Title                : " + reader.Header.GetAttribute("Title"));
        Console.WriteLine("Engine version       : " + reader.Header.GetAttribute("GeneratedByEngineVersion"));
        Console.WriteLine("Encrypted            : " + reader.Header.GetAttribute("Encrypted"));
        Console.WriteLine("Header checksum      : PASS");
        Console.WriteLine("Key blocks           : " + reader.Keys.BlockCount);
        Console.WriteLine("Resource keys        : " + reader.Keys.AllEntries.Count);
        Console.WriteLine("Key Info checksum    : PASS");
        Console.WriteLine("Decoded Key blocks   : " +
            (reader.Keys.ZlibBlockCount + reader.Keys.UncompressedBlockCount));
        Console.WriteLine("Record blocks        : " + reader.Records.BlockCount);
        Console.WriteLine("Record bytes (plain) : " + reader.Records.TotalDecompressedSize);
        Console.WriteLine("Record index         : PASS");
        if (info)
        {
            Console.WriteLine();
            Console.WriteLine("[First 10 resource keys]");
            for (int i = 0; i < Math.Min(10, reader.Keys.AllEntries.Count); i++)
                Console.WriteLine("  " + reader.Keys.AllEntries[i].Key);
        }
        if (find)
        {
            int limit = 20;
            if (args.Length == 4 &&
                (!int.TryParse(args[3], out limit) || limit < 1 || limit > 100))
                throw new ArgumentException("Search limit must be from 1 to 100.");
            List<string> names = reader.FindResources(args[2], limit);
            Console.WriteLine();
            Console.WriteLine("[Resource matches: " + args[2] + "]");
            Console.WriteLine("Matches              : " + names.Count + " (max " + limit + ")");
            for (int i = 0; i < names.Count; i++)
                Console.WriteLine("  " + names[i]);
        }
        if (get)
        {
            byte[] data = reader.GetResource(args[2]);
            if (data == null)
            {
                Console.WriteLine("Resource not found: " + args[2]);
                return 3;
            }
            Console.WriteLine();
            Console.WriteLine("[Resource extraction]");
            Console.WriteLine("Resource key         : " + args[2]);
            Console.WriteLine("Bytes                : " + data.Length);
            Console.WriteLine("SHA-256              : " +
                Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
            // Output path is supplied explicitly by the user; never use an MDD key as a filesystem path.
            File.WriteAllBytes(args[3], data);
            Console.WriteLine("Saved to             : " + Path.GetFullPath(args[3]));
        }
        return 0;
    }

    private static void PrintHeader(MdictHeader header)
    {
        Console.WriteLine("MdictReaderTest v0.6");
        Console.WriteLine();
        Console.WriteLine("[File]");
        Console.WriteLine("Path                 : " + header.FilePath);
        Console.WriteLine("File size (bytes)    : " + header.FileLength);
        Console.WriteLine("Root element         : " + header.RootName);
        Console.WriteLine();
        Console.WriteLine("[Header diagnostics]");
        Console.WriteLine("Header bytes         : " + header.HeaderByteCount);
        Console.WriteLine("Next section offset  : " + header.KeySectionOffset);
        Console.WriteLine("Stored Adler-32      : 0x" + header.StoredChecksum.ToString("X8"));
        Console.WriteLine("Computed Adler-32    : 0x" + header.ComputedChecksum.ToString("X8"));
        Console.WriteLine("Checksum             : PASS");
        Console.WriteLine();
        Console.WriteLine("[Main attributes]");
        Console.WriteLine("Title                : " + ShortValue(header.GetAttribute("Title")));
        Console.WriteLine("Engine version       : " + header.GetAttribute("GeneratedByEngineVersion"));
        Console.WriteLine("Required version     : " + header.GetAttribute("RequiredEngineVersion"));
        Console.WriteLine("Encoding             : " + header.GetAttribute("Encoding"));
        Console.WriteLine("Encrypted            : " + header.GetAttribute("Encrypted"));
        Console.WriteLine("KeyCaseSensitive     : " + header.GetAttribute("KeyCaseSensitive"));
        Console.WriteLine("StripKey             : " + header.GetAttribute("StripKey"));
        Console.WriteLine("Format               : " + header.GetAttribute("Format"));
        Console.WriteLine();
        Console.WriteLine("[All header attributes]");
        foreach (KeyValuePair<string, string> item in header.Attributes)
        {
            string value = item.Key.Equals("RegCode", StringComparison.OrdinalIgnoreCase)
                ? "(hidden)" : ShortValue(item.Value);
            Console.WriteLine(item.Key + " = " + value);
        }
        Console.WriteLine("Header reading completed.");
    }

    private static void PrintKeySection(KeySectionInfo section)
    {
        Console.WriteLine();
        Console.WriteLine("[Key Section preamble]");
        Console.WriteLine("Key blocks           : " + section.BlockCount);
        Console.WriteLine("Key entries          : " + section.EntryCount);
        Console.WriteLine("Key Info packed      : " + section.KeyInfoCompressedSize);
        Console.WriteLine("Key Info unpacked    : " + section.KeyInfoDecompressedSize);
        Console.WriteLine("Key Blocks bytes     : " + section.KeyBlocksTotalSize);
        Console.WriteLine("Key Info offset      : " + section.KeyInfoFileOffset);
        Console.WriteLine("Key Blocks offset    : " + section.KeyBlocksFileOffset);
        Console.WriteLine("Record Section offset: " + section.RecordSectionOffset);
        Console.WriteLine("Preamble checksum    : PASS (0x" + section.StoredPreambleChecksum.ToString("X8") + ")");
        Console.WriteLine("Key Info encrypted   : " + (section.KeyInfoEncrypted ? "Yes (flag 2)" : "No"));
        Console.WriteLine("Key Info checksum    : PASS (0x" + section.StoredKeyInfoChecksum.ToString("X8") + ")");
        Console.WriteLine("Block metadata count : " + section.Blocks.Count);

        Console.WriteLine();
        Console.WriteLine("[First key blocks]");
        int blockCount = Math.Min(section.Blocks.Count, 5);
        for (int i = 0; i < blockCount; i++)
        {
            KeyBlockInfo block = section.Blocks[i];
            Console.WriteLine("Block " + i + " : " + block.EntryCount + " keys; " +
                block.CompressedSize + " / " + block.DecompressedSize + " bytes; offset " + block.FileOffset);
            Console.WriteLine("  First: " + block.FirstKey);
            Console.WriteLine("  Last : " + block.LastKey);
        }

        Console.WriteLine();
        Console.WriteLine("[First Key Block]");
        string compression = section.FirstBlockCompression == 0 ? "None" :
            section.FirstBlockCompression == 2 ? "Zlib" : "Type " + section.FirstBlockCompression;
        Console.WriteLine("Compression          : " + compression);
        Console.WriteLine("Checksum             : PASS (0x" + section.FirstBlockStoredChecksum.ToString("X8") + ")");
        Console.WriteLine("Parsed entries       : " + section.FirstBlockEntries.Count);
        Console.WriteLine("First 20 keys and record offsets:");
        for (int i = 0; i < Math.Min(20, section.FirstBlockEntries.Count); i++)
        {
            KeyEntry entry = section.FirstBlockEntries[i];
            Console.WriteLine("  " + (i + 1).ToString("D2") + "  " + entry.RecordOffset + "  " + entry.Key);
        }
        Console.WriteLine();
        if (!section.AllKeysLoaded)
            Console.WriteLine("First block only: use the normal command to read all keys.");
    }

    private static void PrintFullIndex(KeySectionInfo section)
    {
        Console.WriteLine();
        Console.WriteLine("[Full Key Index]");
        Console.WriteLine("Decoded blocks       : " +
            (section.ZlibBlockCount + section.UncompressedBlockCount));
        Console.WriteLine("Zlib blocks          : " + section.ZlibBlockCount);
        Console.WriteLine("Uncompressed blocks  : " + section.UncompressedBlockCount);
        Console.WriteLine("Expected entries     : " + section.EntryCount);
        Console.WriteLine("Loaded entries       : " + section.AllEntries.Count);
        Console.WriteLine("Entry count          : PASS");
        Console.WriteLine("Record offsets       : PASS (globally non-decreasing)");
        Console.WriteLine("Repeated exact keys  : " + section.DuplicateExactKeyCount +
            " (case-insensitive; all retained)");
        Console.WriteLine("First record offset  : " + section.AllEntries[0].RecordOffset);
        Console.WriteLine("Last record offset   : " +
            section.AllEntries[section.AllEntries.Count - 1].RecordOffset);
        Console.WriteLine("First metadata key   : " +
            (section.Blocks[0].FirstKey.Length == 0 ? "(empty; can occur with StripKey)" :
            section.Blocks[0].FirstKey));

        Console.WriteLine();
        Console.WriteLine("[First 10 keys]");
        for (int i = 0; i < Math.Min(10, section.AllEntries.Count); i++)
            PrintEntry(i, section.AllEntries[i]);
        Console.WriteLine();
        Console.WriteLine("[Last 10 keys]");
        int start = Math.Max(0, section.AllEntries.Count - 10);
        for (int i = start; i < section.AllEntries.Count; i++)
            PrintEntry(i, section.AllEntries[i]);

        Console.WriteLine();
        Console.WriteLine("[Sample exact-key checks]");
        string[] examples = { "apple", "trash", "commentator", "convict", "nonexistent-word-xyz-987" };
        for (int i = 0; i < examples.Length; i++)
        {
            KeyEntry entry = section.FindExact(examples[i]);
            Console.WriteLine("  " + examples[i] + " : " +
                (entry == null ? "NOT FOUND" : "FOUND; record offset " + entry.RecordOffset));
        }
        Console.WriteLine();
        Console.WriteLine("Key index reading completed (v0.3 compatibility).");
    }

    private static void PrintEntry(int index, KeyEntry entry)
    {
        Console.WriteLine("  " + (index + 1).ToString("D6") + "  " +
            entry.RecordOffset + "  " + entry.Key);
    }

    private static void PrintExactMatches(KeySectionInfo section, string word)
    {
        Console.WriteLine();
        Console.WriteLine("[Exact lookup: " + word + "]");
        KeyEntry first = section.FindExact(word);
        if (first == null)
        {
            Console.WriteLine("NOT FOUND");
            return;
        }
        Console.WriteLine("First match record offset: " + first.RecordOffset);
        List<KeyEntry> entries = section.FindAllExact(word, 30);
        Console.WriteLine("Matching keys (up to 30):");
        for (int i = 0; i < entries.Count; i++)
            Console.WriteLine("  " + entries[i].RecordOffset + "  " + entries[i].Key);
    }

    private static void PrintPrefixMatches(KeySectionInfo section, string word, int limit)
    {
        Console.WriteLine();
        Console.WriteLine("[Prefix lookup: " + word + "]");
        List<KeyEntry> entries = section.FindPrefix(word, limit);
        Console.WriteLine("Matches (up to " + limit + "): " + entries.Count);
        for (int i = 0; i < entries.Count; i++)
            Console.WriteLine("  " + entries[i].RecordOffset + "  " + entries[i].Key);
    }

    private static void PrintRecordInfo(RecordSectionInfo records)
    {
        Console.WriteLine();
        Console.WriteLine("[Record Section]");
        Console.WriteLine("Record blocks        : " + records.BlockCount);
        Console.WriteLine("Record entries       : " + records.EntryCount);
        Console.WriteLine("Record Info bytes    : " + records.RecordInfoSize);
        Console.WriteLine("Record Blocks bytes  : " + records.RecordBlocksTotalSize);
        Console.WriteLine("Record Blocks offset : " + records.RecordBlocksFileOffset);
        Console.WriteLine("Uncompressed bytes   : " + records.TotalDecompressedSize);
        Console.WriteLine("Index ranges         : PASS");
        Console.WriteLine("First block metadata:");
        for (int i = 0; i < Math.Min(3, records.Blocks.Count); i++)
        {
            RecordBlockInfo block = records.Blocks[i];
            Console.WriteLine("  #" + i + " file=" + block.FileOffset +
                " packed=" + block.CompressedSize + " unpacked=" + block.DecompressedSize +
                " logical=" + block.DecompressedStartOffset);
        }
        Console.WriteLine("NOTE: Record block data is decoded only for --lookup.");
    }

    private static void PrintHtmlLookup(OxfordRecordReader reader, MdictHeader header,
        KeySectionInfo keys, RecordSectionInfo records, string word, string outputPath)
    {
        Console.WriteLine();
        Console.WriteLine("[HTML lookup: " + word + "]");
        int index = keys.FindExactIndex(word);
        if (index < 0)
        {
            Console.WriteLine("NOT FOUND");
            return;
        }
        KeyEntry entry = keys.AllEntries[index];
        string html = reader.ReadHtml(header.FilePath, keys, records, index);
        byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
        Console.WriteLine("Matched key          : " + entry.Key);
        Console.WriteLine("Key index            : " + index);
        Console.WriteLine("Record offset        : " + entry.RecordOffset);
        Console.WriteLine("HTML UTF-8 bytes     : " + htmlBytes.Length);
        Console.WriteLine("HTML SHA-256         : " + Convert.ToHexString(SHA256.HashData(htmlBytes)).ToLowerInvariant());
        Console.WriteLine("HTML preview (first 500 chars):");
        string preview = html.Substring(0, Math.Min(500, html.Length));
        Console.WriteLine(preview.Replace("\r", "\\r").Replace("\n", "\\n"));
        if (outputPath != null)
        {
            File.WriteAllBytes(outputPath, htmlBytes);
            Console.WriteLine("Saved original HTML fragment to: " + Path.GetFullPath(outputPath));
        }
    }

    private static string ShortValue(string value)
    {
        string text = value.Replace("\r", "\\r").Replace("\n", "\\n");
        if (text.Length > 240)
            return text.Substring(0, 240) + "... (truncated, length " + text.Length + ")";
        return text;
    }
}
}
