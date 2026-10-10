using Mdict.Core;
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text;

namespace MdictReaderTest
{
// Small deterministic self-tests, including a synthetic encrypted MDict v2 Key Info.
internal class SelfTest
{
    public static int Run()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "MdictReaderTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            OxfordHeaderReader headerReader = new OxfordHeaderReader();
            string mdxPath = Path.Combine(directory, "sample.mdx");
            WriteSample(mdxPath, "<Dictionary GeneratedByEngineVersion=\"2.0\" " +
                "Encoding=\"UTF-8\" Encrypted=\"No\" Title=\"Test &amp; Verify\" />\r\n\0");
            MdictHeader mdx = headerReader.Read(mdxPath);
            Assert(mdx.RootName == "Dictionary" && mdx.GetAttribute("Title") == "Test & Verify" &&
                   mdx.KeySectionOffset == mdx.FileLength - 3, "MDX header");
            Console.WriteLine("PASS: MDX sample header and checksum");

            string mddPath = Path.Combine(directory, "sample.mdd");
            WriteSample(mddPath, "<Library_Data GeneratedByEngineVersion=\"2.0\" " +
                "Title=\"Test resource library\" />\r\n\0");
            MdictHeader mdd = headerReader.Read(mddPath);
            Assert(mdd.RootName == "Library_Data" &&
                   mdd.GetAttribute("Title") == "Test resource library", "MDD header");
            Console.WriteLine("PASS: MDD sample header and checksum");

            byte[] damaged = File.ReadAllBytes(mdxPath);
            damaged[damaged.Length - 4] ^= 1;
            File.WriteAllBytes(mdxPath, damaged);
            ExpectInvalidData(delegate { headerReader.Read(mdxPath); }, "Corrupted checksum");
            Console.WriteLine("PASS: Corrupted checksum rejected");

            string truncatedPath = Path.Combine(directory, "truncated.mdx");
            File.WriteAllBytes(truncatedPath, new byte[] { 0, 1 });
            ExpectInvalidData(delegate { headerReader.Read(truncatedPath); }, "Truncated file");
            Console.WriteLine("PASS: Truncated file rejected");

            CheckHash("", "cdf26213a150dc3ecb610f18f6b38b46");
            CheckHash("a", "86be7afa339d0fc7cfc785e72f578d33");
            CheckHash("abc", "c14a12199c66e4ba84636b0f69144c77");
            CheckHash("message digest", "9e327b3d6e523062afc1132d7df9d1b8");
            Console.WriteLine("PASS: RIPEMD-128 published test vectors");

            string testMdxPath = Path.Combine(directory, "keys.mdx");
            WriteV2Sample(testMdxPath, true);
            MdictHeader encryptedHeader = headerReader.Read(testMdxPath);
            KeySectionInfo encryptedKeys = new OxfordKeyReader().Read(encryptedHeader);
            CheckKeySection(encryptedKeys, true);
            Console.WriteLine("PASS: Encrypted=2 Key Info, zlib and first Key Block");

            WriteV2Sample(testMdxPath, false);
            MdictHeader plainHeader = headerReader.Read(testMdxPath);
            KeySectionInfo plainKeys = new OxfordKeyReader().Read(plainHeader);
            CheckKeySection(plainKeys, false);
            Console.WriteLine("PASS: Unencrypted Key Info, zlib and first Key Block");

            byte[] invalid = File.ReadAllBytes(testMdxPath);
            long keyInfoOffset = plainKeys.KeyInfoFileOffset;
            invalid[(int)keyInfoOffset + 4] ^= 0x40;
            File.WriteAllBytes(testMdxPath, invalid);
            ExpectInvalidData(delegate { new OxfordKeyReader().Read(headerReader.Read(testMdxPath)); },
                "Damaged Key Info checksum");
            Console.WriteLine("PASS: Damaged Key Info checksum rejected");

            string multiPath = Path.Combine(directory, "threeblocks.mdx");
            WriteThreeBlockSample(multiPath, false);
            MdictHeader multiHeader = headerReader.Read(multiPath);
            KeySectionInfo all = new OxfordKeyReader().Read(multiHeader, true);
            Assert(all.AllKeysLoaded && all.AllEntries.Count == 5 && all.Blocks.Count == 3,
                "All three synthetic Key Blocks loaded");
            Assert(all.ZlibBlockCount == 2 && all.UncompressedBlockCount == 1,
                "Both zlib and uncompressed blocks decoded");
            Assert(all.AllEntries[0].Key == "%" && all.Blocks[0].FirstKey == "" &&
                   all.AllEntries[4].Key == "café" && all.AllEntries[4].RecordOffset == 120,
                "Empty normalized metadata key and original UTF-8 words");
            Console.WriteLine("PASS: All-key index across three blocks (zlib/none, UTF-8)");

            Assert(all.DuplicateExactKeyCount == 1 &&
                   all.FindExact("APPLE").RecordOffset == 42 &&
                   all.FindAllExact("apple", 20).Count == 2 &&
                   all.FindPrefix("ap", 10).Count == 2 &&
                   all.FindExact("absent") == null,
                "All-key exact and prefix searches");
            Console.WriteLine("PASS: Exact/prefix search and repeated keys retained");

            byte[] damagedBlock = File.ReadAllBytes(multiPath);
            damagedBlock[(int)all.Blocks[2].FileOffset + 4] ^= 0x20;
            File.WriteAllBytes(multiPath, damagedBlock);
            ExpectInvalidData(delegate { new OxfordKeyReader().Read(headerReader.Read(multiPath), true); },
                "Damaged later Key Block checksum");
            Console.WriteLine("PASS: Damaged later Key Block checksum rejected");

            WriteThreeBlockSample(multiPath, true);
            ExpectInvalidData(delegate { new OxfordKeyReader().Read(headerReader.Read(multiPath), true); },
                "Descending offset across Key Blocks");
            Console.WriteLine("PASS: Descending record offset across blocks rejected");

            // The synthetic Record Section has three blocks (zlib, none, zlib).
            // Its record boundaries deliberately cross block boundaries.
            WriteThreeBlockSample(multiPath, false);
            AppendRecordSection(multiPath);
            multiHeader = headerReader.Read(multiPath);
            all = new OxfordKeyReader().Read(multiHeader, true);
            OxfordRecordReader recordReader = new OxfordRecordReader();
            RecordSectionInfo records = recordReader.ReadIndex(multiHeader, all);
            Assert(records.BlockCount == 3 && records.EntryCount == 5 &&
                   records.TotalDecompressedSize == 152 && records.Blocks[1].DecompressedStartOffset == 30 &&
                   records.Blocks[2].DecompressedStartOffset == 90,
                "Record preamble, physical and logical block locations");
            Console.WriteLine("PASS: Record Section metadata and logical offsets");

            Assert(recordReader.ReadHtml(multiPath, all, records, 0) == "<p>percent</p>" &&
                   recordReader.ReadHtml(multiPath, all, records, 1) == "<p>apple 苹果</p>" &&
                   recordReader.ReadHtml(multiPath, all, records, 2) == "<p>apple 苹果</p>" &&
                   recordReader.ReadHtml(multiPath, all, records, 3) == "<p>banana</p>" &&
                   recordReader.ReadHtml(multiPath, all, records, 4) == "<p>café</p>",
                "Cross-block HTML, duplicate offsets, UTF-8 and final record");
            Console.WriteLine("PASS: Cross-block HTML, shared offset, UTF-8 and final record");

            byte[] damagedRecord = File.ReadAllBytes(multiPath);
            damagedRecord[checked((int)records.Blocks[2].FileOffset + 4)] ^= 0x10;
            File.WriteAllBytes(multiPath, damagedRecord);
            ExpectInvalidData(delegate { recordReader.ReadHtml(multiPath, all, records, 4); },
                "Damaged later Record Block checksum");
            Console.WriteLine("PASS: Damaged Record Block checksum rejected");

            WriteThreeBlockSample(multiPath, false);
            AppendRecordSection(multiPath);
            byte[] damagedInfo = File.ReadAllBytes(multiPath);
            // Record Section header: block count, entry count, info length, data length.
            damagedInfo[checked((int)all.RecordSectionOffset + 23)] ^= 0x01;
            File.WriteAllBytes(multiPath, damagedInfo);
            ExpectInvalidData(delegate
            {
                recordReader.ReadIndex(headerReader.Read(multiPath), all);
            }, "Damaged Record Block Info size");
            Console.WriteLine("PASS: Invalid Record Block Info size rejected");

            // v0.5: synthetic MDD with UTF-16LE resource keys and binary records.
            string resourcePath = Path.Combine(directory, "resources.mdd");
            byte[] css = Encoding.UTF8.GetBytes("body { color: red; }");
            byte[] png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 255 };
            byte[] mp3 = new byte[] { 0x49, 0x44, 0x33, 0x01, 0x00, 0x7F, 0xFF };
            WriteMddSample(resourcePath, css, png, mp3);
            OxfordMddReader resourceReader = new OxfordMddReader(resourcePath);
            Assert(resourceReader.Header.RootName == "Library_Data" &&
                   resourceReader.Keys.AllEntries.Count == 3 &&
                   resourceReader.Keys.AllEntries[1].Key == "\\images\\苹果.png" &&
                   resourceReader.Records.BlockCount == 2,
                   "MDD UTF-16LE keys and block counts");
            Console.WriteLine("PASS: MDD UTF-16LE resource keys and index");

            Assert(SameBytes(resourceReader.GetResource("/oald10.css"), css) &&
                   SameBytes(resourceReader.GetResource("images/苹果.png"), png) &&
                   SameBytes(resourceReader.GetResource("\\sound\\apple__gb_5.mp3"), mp3),
                   "MDD CSS / PNG / MP3 byte-for-byte and cross-block resource");
            Console.WriteLine("PASS: MDD CSS, PNG, MP3 exact bytes (cross-block)");

            Assert(resourceReader.FindResources("apple", 10).Count == 1 &&
                   resourceReader.GetResource("missing.mp3") == null,
                   "MDD resource search and not-found");
            Console.WriteLine("PASS: MDD resource find and missing key");

            byte[] corruptedMdd = File.ReadAllBytes(resourcePath);
            corruptedMdd[checked((int)resourceReader.Records.Blocks[1].FileOffset + 4)] ^= 0x20;
            File.WriteAllBytes(resourcePath, corruptedMdd);
            ExpectInvalidData(delegate { resourceReader.GetResource("/sound/apple__gb_5.mp3"); },
                "Corrupted MDD Record Block");
            Console.WriteLine("PASS: Damaged MDD Record Block checksum rejected");

            // v0.6: exact same-basename MDD discovery and cross-volume extraction.
            // Intentionally include a gap (.2 missing) and an unrelated MDD file.
            WriteThreeBlockSample(multiPath, false);
            AppendRecordSection(multiPath);
            string stem = Path.Combine(directory, "threeblocks");
            string mainVolume = stem + ".mdd";
            string volume1 = stem + ".1.mdd";
            string volume3 = stem + ".3.mdd";
            WriteMddSample(mainVolume, css, png, mp3,
                new string[] { "\\oald10.css", "\\images\\苹果.png", "\\sound\\other.mp3" });
            WriteMddSample(volume1, css, png, mp3,
                new string[] { "\\extra.css", "\\images\\pear.png", "\\sound\\apple__gb_5.mp3" });
            WriteMddSample(volume3, css, png, mp3,
                new string[] { "\\extra3.css", "\\images\\cat.png", "\\sound\\apple__us_1.mp3" });
            File.WriteAllBytes(Path.Combine(directory, "unrelated.mdd"), new byte[] { 0 });
            File.WriteAllBytes(stem + ".01.mdd", new byte[] { 0 });
            File.WriteAllBytes(stem + ".4.mdd.bak", new byte[] { 0 });
            OxfordDictionary dictionary = new OxfordDictionary(multiPath);
            Assert(dictionary.MddPaths.Count == 3 &&
                dictionary.MddPaths[0] == mainVolume &&
                dictionary.MddPaths[1] == volume1 &&
                dictionary.MddPaths[2] == volume3,
                "Matching MDX basename, numbered ordering, gaps and unrelated file exclusion");
            Console.WriteLine("PASS: Oxford matching MDD volumes, order and exclusion");

            Assert(dictionary.Lookup("APPLE") == "<p>apple 苹果</p>" &&
                dictionary.SearchPrefix("ap", 10).Count == 2 &&
                dictionary.Lookup("not-there") == null,
                "Oxford MDX lookup and prefix through small facade");
            Console.WriteLine("PASS: Oxford MDX lookup and prefix through facade");

            OxfordResourceMatch match;
            Assert(SameBytes(dictionary.GetResource("oald10.css", out match), css) &&
                match.MddPath == mainVolume &&
                SameBytes(dictionary.GetResource("sound://apple__gb_5.mp3", out match), mp3) &&
                match.MddPath == volume1 && match.Key == "\\sound\\apple__gb_5.mp3" &&
                SameBytes(dictionary.GetResource("/sound/apple__us_1.mp3", out match), mp3) &&
                match.MddPath == volume3 &&
                dictionary.GetResource("missing.mp3", out match) == null,
                "Resource discovery across matching volumes, sound scheme and missing keys");
            Console.WriteLine("PASS: Oxford resource retrieval from several MDD volumes");

            List<OxfordResourceMatch> combined = dictionary.FindResources("apple__", 10);
            Assert(combined.Count == 2 && combined[0].MddPath == volume1 &&
                combined[1].MddPath == volume3,
                "Resource substring search keeps originating MDD volume");
            // Make two different subdirectories hold the same audio basename.
            WriteMddSample(volume3, css, png, mp3,
                new string[] { "\\extra3.css", "\\images\\cat.png", "\\alternate\\apple__gb_5.mp3" });
            OxfordDictionary ambiguous = new OxfordDictionary(multiPath);
            bool rejected = false;
            try { ambiguous.GetResource("sound://apple__gb_5.mp3", out match); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected && SameBytes(ambiguous.GetResource("\\alternate\\apple__gb_5.mp3", out match), mp3),
                "Ambiguous filename rejected; full resource key still works");
            Console.WriteLine("PASS: Oxford find results and ambiguous filename protection");

            Console.WriteLine("All 24 self-tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SELF-TEST FAILED: " + ex.Message);
            return 1;
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void CheckKeySection(KeySectionInfo section, bool encrypted)
    {
        Assert(section.BlockCount == 1 && section.EntryCount == 2 && section.Blocks.Count == 1,
            "Synthetic Key Section counts");
        Assert(section.KeyInfoEncrypted == encrypted, "Key Info encrypted flag");
        Assert(section.Blocks[0].FirstKey == "apple" && section.Blocks[0].LastKey == "banana",
            "Block key range");
        Assert(section.FirstBlockCompression == 2 && section.FirstBlockEntries.Count == 2,
            "Key Block zlib decode");
        Assert(section.FirstBlockEntries[0].Key == "apple" && section.FirstBlockEntries[0].RecordOffset == 0,
            "First Key Entry");
        Assert(section.FirstBlockEntries[1].Key == "banana" && section.FirstBlockEntries[1].RecordOffset == 42,
            "Second Key Entry");
        Assert(section.RecordSectionOffset > section.KeyBlocksFileOffset,
            "Record Section offset");
    }

    private static void CheckHash(string text, string expected)
    {
        string hash = Convert.ToHexString(Ripemd128.Hash(Encoding.ASCII.GetBytes(text))).ToLowerInvariant();
        Assert(hash == expected, "RIPEMD-128: " + text + "; actual " + hash);
    }

    private static void WriteV2Sample(string path, bool encrypted)
    {
        string xml = "<Dictionary GeneratedByEngineVersion=\"2.0\" " +
            "Encoding=\"UTF-8\" Encrypted=\"" + (encrypted ? "2" : "No") + "\" Title=\"Keys\" />\r\n\0";
        WriteSample(path, xml);
        // Remove the v0.1 three-byte fake tail, replacing it with an actual v2 key section.
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            stream.SetLength(stream.Length - 3);
            stream.Position = stream.Length;

            byte[] keyData;
            using (MemoryStream buffer = new MemoryStream())
            using (BinaryWriter keyWriter = new BinaryWriter(buffer, Encoding.UTF8, true))
            {
                WriteUInt64BE(keyWriter, 0);
                keyWriter.Write(Encoding.UTF8.GetBytes("apple\0"));
                WriteUInt64BE(keyWriter, 42);
                keyWriter.Write(Encoding.UTF8.GetBytes("banana\0"));
                keyData = buffer.ToArray();
            }
            byte[] packedKeys = MakePackedBlock(keyData);

            byte[] indexData;
            using (MemoryStream buffer = new MemoryStream())
            using (BinaryWriter indexWriter = new BinaryWriter(buffer, Encoding.UTF8, true))
            {
                WriteUInt64BE(indexWriter, 2);
                WriteUInt16BE(indexWriter, 5);
                indexWriter.Write(Encoding.UTF8.GetBytes("apple\0"));
                WriteUInt16BE(indexWriter, 6);
                indexWriter.Write(Encoding.UTF8.GetBytes("banana\0"));
                WriteUInt64BE(indexWriter, packedKeys.Length);
                WriteUInt64BE(indexWriter, keyData.Length);
                indexData = buffer.ToArray();
            }
            byte[] packedIndex = MakePackedBlock(indexData);
            if (encrypted)
            {
                byte[] seed = new byte[8];
                Buffer.BlockCopy(packedIndex, 4, seed, 0, 4);
                seed[4] = 0x95;
                seed[5] = 0x36;
                byte[] key = Ripemd128.Hash(seed);
                int previous = 0x36;
                for (int i = 8; i < packedIndex.Length; i++)
                {
                    int item = packedIndex[i];
                    int combined = item ^ previous ^ ((i - 8) & 0xff) ^ key[(i - 8) % 16];
                    int ciphertext = ((combined >> 4) | (combined << 4)) & 0xff;
                    packedIndex[i] = (byte)ciphertext;
                    previous = ciphertext;
                }
            }
            byte[] preamble;
            using (MemoryStream buffer = new MemoryStream())
            using (BinaryWriter numbers = new BinaryWriter(buffer))
            {
                WriteUInt64BE(numbers, 1);
                WriteUInt64BE(numbers, 2);
                WriteUInt64BE(numbers, indexData.Length);
                WriteUInt64BE(numbers, packedIndex.Length);
                WriteUInt64BE(numbers, packedKeys.Length);
                preamble = buffer.ToArray();
            }
            writer.Write(preamble);
            WriteUInt32BE(writer, OxfordHeaderReader.CalculateAdler32(preamble));
            writer.Write(packedIndex);
            writer.Write(packedKeys);
            // Dummy record-section bytes are irrelevant for this stage.
            writer.Write(new byte[] { 0, 0, 0, 0 });
        }
    }

    private static byte[] MakePackedBlock(byte[] data)
    {
        byte[] compressed;
        using (MemoryStream output = new MemoryStream())
        {
            using (ZLibStream zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
                zlib.Write(data, 0, data.Length);
            compressed = output.ToArray();
        }
        using (MemoryStream output = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(output))
        {
            writer.Write(new byte[] { 2, 0, 0, 0 });
            WriteUInt32BE(writer, OxfordHeaderReader.CalculateAdler32(data));
            writer.Write(compressed);
            return output.ToArray();
        }
    }

    private static byte[] MakeUncompressedBlock(byte[] data)
    {
        using (MemoryStream output = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(output))
        {
            writer.Write(new byte[] { 0, 0, 0, 0 });
            WriteUInt32BE(writer, OxfordHeaderReader.CalculateAdler32(data));
            writer.Write(data);
            return output.ToArray();
        }
    }

    private static byte[] MakeKeyData(string[] words, long[] offsets)
    {
        using (MemoryStream output = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(output))
        {
            for (int i = 0; i < words.Length; i++)
            {
                WriteUInt64BE(writer, offsets[i]);
                writer.Write(Encoding.UTF8.GetBytes(words[i] + "\0"));
            }
            return output.ToArray();
        }
    }

    private static void WriteIndexWord(BinaryWriter writer, string word)
    {
        byte[] data = Encoding.UTF8.GetBytes(word);
        WriteUInt16BE(writer, data.Length);
        writer.Write(data);
        writer.Write((byte)0);
    }

    private static void WriteThreeBlockSample(string path, bool descendingOffset)
    {
        string xml = "<Dictionary GeneratedByEngineVersion=\"2.0\" " +
            "Encoding=\"UTF-8\" Encrypted=\"2\" StripKey=\"Yes\" Title=\"ThreeBlocks\" />\r\n\0";
        WriteSample(path, xml);
        byte[][] keyData =
        {
            MakeKeyData(new string[] { "%", "Apple" }, new long[] { 0, 42 }),
            MakeKeyData(new string[] { "apple", "banana" }, new long[] { 42, 73 }),
            MakeKeyData(new string[] { "café" }, new long[] { descendingOffset ? 70 : 120 })
        };
        byte[][] blocks =
        {
            MakePackedBlock(keyData[0]),
            MakeUncompressedBlock(keyData[1]),
            MakePackedBlock(keyData[2])
        };
        byte[] indexData;
        using (MemoryStream buffer = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(buffer))
        {
            string[] starts = { "", "apple", "café" };
            string[] ends = { "Apple", "banana", "café" };
            long[] counts = { 2, 2, 1 };
            for (int i = 0; i < blocks.Length; i++)
            {
                WriteUInt64BE(writer, counts[i]);
                WriteIndexWord(writer, starts[i]);
                WriteIndexWord(writer, ends[i]);
                WriteUInt64BE(writer, blocks[i].Length);
                WriteUInt64BE(writer, keyData[i].Length);
            }
            indexData = buffer.ToArray();
        }
        byte[] packedIndex = MakePackedBlock(indexData);
        // Encrypt only the compressed Key Info payload (same as v0.2 fixture).
        byte[] seed = new byte[8];
        Buffer.BlockCopy(packedIndex, 4, seed, 0, 4);
        seed[4] = 0x95;
        seed[5] = 0x36;
        byte[] key = Ripemd128.Hash(seed);
        int previous = 0x36;
        for (int i = 8; i < packedIndex.Length; i++)
        {
            int position = i - 8;
            int original = packedIndex[i];
            int combined = original ^ previous ^ (position & 0xff) ^ key[position % 16];
            int ciphertext = ((combined >> 4) | (combined << 4)) & 0xff;
            packedIndex[i] = (byte)ciphertext;
            previous = ciphertext;
        }
        byte[] preamble;
        using (MemoryStream buffer = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(buffer))
        {
            WriteUInt64BE(writer, 3);
            WriteUInt64BE(writer, 5);
            WriteUInt64BE(writer, indexData.Length);
            WriteUInt64BE(writer, packedIndex.Length);
            WriteUInt64BE(writer, blocks[0].Length + blocks[1].Length + blocks[2].Length);
            preamble = buffer.ToArray();
        }
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            stream.SetLength(stream.Length - 3);
            stream.Position = stream.Length;
            writer.Write(preamble);
            WriteUInt32BE(writer, OxfordHeaderReader.CalculateAdler32(preamble));
            writer.Write(packedIndex);
            for (int i = 0; i < blocks.Length; i++)
                writer.Write(blocks[i]);
            writer.Write(new byte[] { 0, 0, 0, 0 });
        }
    }

    private static void AppendRecordSection(string path)
    {
        // Global uncompressed offsets: 0, 42, 42, 73, 120; end=152.
        // Three Record Blocks cover [0,30), [30,90), [90,152).
        // Both '%' and 'banana' cross block boundaries.
        byte[] data = new byte[152];
        PutHtml(data, 0, "<p>percent</p>");
        PutHtml(data, 42, "<p>apple 苹果</p>");
        PutHtml(data, 73, "<p>banana</p>");
        PutHtml(data, 120, "<p>café</p>");
        byte[] first = new byte[30];
        byte[] second = new byte[60];
        byte[] third = new byte[62];
        Buffer.BlockCopy(data, 0, first, 0, 30);
        Buffer.BlockCopy(data, 30, second, 0, 60);
        Buffer.BlockCopy(data, 90, third, 0, 62);
        byte[][] packed = {
            MakePackedBlock(first), MakeUncompressedBlock(second), MakePackedBlock(third)
        };
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            stream.SetLength(stream.Length - 4); // replace the v0.3 placeholder
            stream.Position = stream.Length;
            WriteUInt64BE(writer, 3); // Record Block count
            WriteUInt64BE(writer, 5); // Number of keys
            WriteUInt64BE(writer, 3 * 16); // two uint64 values per block
            WriteUInt64BE(writer, packed[0].Length + packed[1].Length + packed[2].Length);
            for (int i = 0; i < packed.Length; i++)
            {
                WriteUInt64BE(writer, packed[i].Length);
                WriteUInt64BE(writer, i == 0 ? 30 : i == 1 ? 60 : 62);
            }
            for (int i = 0; i < packed.Length; i++)
                writer.Write(packed[i]);
        }
    }

    private static void PutHtml(byte[] buffer, int position, string html)
    {
        byte[] content = Encoding.UTF8.GetBytes(html);
        Buffer.BlockCopy(content, 0, buffer, position, content.Length);
    }

    private static void WriteUInt64BE(BinaryWriter writer, long value)
    {
        ulong number = (ulong)value;
        for (int i = 7; i >= 0; i--)
            writer.Write((byte)(number >> (i * 8)));
    }

    private static void WriteUInt32BE(BinaryWriter writer, uint value)
    {
        for (int i = 3; i >= 0; i--)
            writer.Write((byte)(value >> (i * 8)));
    }

    private static void WriteUInt16BE(BinaryWriter writer, int value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteSample(string path, string headerText)
    {
        byte[] data = Encoding.Unicode.GetBytes(headerText);
        uint size = (uint)data.Length;
        uint checksum = OxfordHeaderReader.CalculateAdler32(data);
        using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            stream.WriteByte((byte)(size >> 24));
            stream.WriteByte((byte)(size >> 16));
            stream.WriteByte((byte)(size >> 8));
            stream.WriteByte((byte)size);
            stream.Write(data, 0, data.Length);
            stream.WriteByte((byte)checksum);
            stream.WriteByte((byte)(checksum >> 8));
            stream.WriteByte((byte)(checksum >> 16));
            stream.WriteByte((byte)(checksum >> 24));
            stream.WriteByte(1);
            stream.WriteByte(2);
            stream.WriteByte(3);
        }
    }


    private static bool SameBytes(byte[] first, byte[] second)
    {
        if (first == null || second == null || first.Length != second.Length)
            return false;
        for (int i = 0; i < first.Length; i++)
        {
            if (first[i] != second[i])
                return false;
        }
        return true;
    }

    private static void WriteMddSample(string path, byte[] css, byte[] png, byte[] mp3, string[] customNames = null)
    {
        // Header Encoding deliberately says UTF-8; MDD resource keys are UTF-16LE regardless.
        WriteSample(path, "<Library_Data GeneratedByEngineVersion=\"2.0\" " +
            "Encoding=\"UTF-8\" Encrypted=\"2\" Title=\"Resource Sample\" />\r\n\0");
        string[] names = customNames ?? new string[] { "\\oald10.css", "\\images\\苹果.png", "\\sound\\apple__gb_5.mp3" };
        long[] offsets = { 0, css.Length, css.Length + png.Length };
        byte[] keys;
        using (MemoryStream buffer = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(buffer))
        {
            for (int i = 0; i < names.Length; i++)
            {
                WriteUInt64BE(writer, offsets[i]);
                writer.Write(Encoding.Unicode.GetBytes(names[i] + "\0"));
            }
            keys = buffer.ToArray();
        }
        byte[] packedKeys = MakePackedBlock(keys);
        byte[] info;
        using (MemoryStream buffer = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(buffer))
        {
            WriteUInt64BE(writer, names.Length);
            WriteIndexWordUtf16(writer, names[0]);
            WriteIndexWordUtf16(writer, names[names.Length - 1]);
            WriteUInt64BE(writer, packedKeys.Length);
            WriteUInt64BE(writer, keys.Length);
            info = buffer.ToArray();
        }
        byte[] packedInfo = MakePackedBlock(info);
        // Encrypt the compressed Key Info bytes using the v2 fast-encrypt inverse.
        byte[] seed = new byte[8];
        Buffer.BlockCopy(packedInfo, 4, seed, 0, 4);
        seed[4] = 0x95;
        seed[5] = 0x36;
        byte[] key = Ripemd128.Hash(seed);
        int previous = 0x36;
        for (int i = 8; i < packedInfo.Length; i++)
        {
            int pos = i - 8;
            int combined = packedInfo[i] ^ previous ^ (pos & 255) ^ key[pos % 16];
            int cipher = ((combined >> 4) | (combined << 4)) & 255;
            packedInfo[i] = (byte)cipher;
            previous = cipher;
        }
        byte[] preamble;
        using (MemoryStream buffer = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(buffer))
        {
            WriteUInt64BE(writer, 1);
            WriteUInt64BE(writer, names.Length);
            WriteUInt64BE(writer, info.Length);
            WriteUInt64BE(writer, packedInfo.Length);
            WriteUInt64BE(writer, packedKeys.Length);
            preamble = buffer.ToArray();
        }

        byte[] content = new byte[css.Length + png.Length + mp3.Length];
        Buffer.BlockCopy(css, 0, content, 0, css.Length);
        Buffer.BlockCopy(png, 0, content, css.Length, png.Length);
        Buffer.BlockCopy(mp3, 0, content, css.Length + png.Length, mp3.Length);
        int split = css.Length + 2; // the image crosses the Record Block boundary
        byte[] record1 = new byte[split];
        byte[] record2 = new byte[content.Length - split];
        Buffer.BlockCopy(content, 0, record1, 0, record1.Length);
        Buffer.BlockCopy(content, split, record2, 0, record2.Length);
        byte[] packed1 = MakePackedBlock(record1);
        byte[] packed2 = MakeUncompressedBlock(record2);
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            stream.SetLength(stream.Length - 3);
            stream.Position = stream.Length;
            writer.Write(preamble);
            WriteUInt32BE(writer, OxfordHeaderReader.CalculateAdler32(preamble));
            writer.Write(packedInfo);
            writer.Write(packedKeys);
            WriteUInt64BE(writer, 2); // Record Block count
            WriteUInt64BE(writer, names.Length);
            WriteUInt64BE(writer, 32); // two blocks, 16 bytes per block
            WriteUInt64BE(writer, packed1.Length + packed2.Length);
            WriteUInt64BE(writer, packed1.Length);
            WriteUInt64BE(writer, record1.Length);
            WriteUInt64BE(writer, packed2.Length);
            WriteUInt64BE(writer, record2.Length);
            writer.Write(packed1);
            writer.Write(packed2);
        }
    }

    private static void WriteIndexWordUtf16(BinaryWriter writer, string word)
    {
        WriteUInt16BE(writer, word.Length);
        writer.Write(Encoding.Unicode.GetBytes(word + "\0"));
    }

    private static void ExpectInvalidData(Action operation, string name)
    {
        bool failed = false;
        try { operation(); }
        catch (InvalidDataException) { failed = true; }
        Assert(failed, name);
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition)
            throw new Exception("Assertion failed: " + name);
    }
}
}
