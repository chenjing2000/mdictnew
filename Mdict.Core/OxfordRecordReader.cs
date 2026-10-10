using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Mdict.Core
{
// v0.5: MDict 2.0 Record Section reader for Oxford MDX and MDD.
// Reads only requested Record Blocks; all Record offsets are in the uncompressed stream.
public class OxfordRecordReader
{
    private const int MaxPackedBlock = 64 * 1024 * 1024;
    private const int MaxPlainBlock = 64 * 1024 * 1024;
    private const long MaxSingleRecord = 64L * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public RecordSectionInfo ReadIndex(MdictHeader header, KeySectionInfo keys)
    {
        bool isMdd = header.FilePath.EndsWith(".mdd", StringComparison.OrdinalIgnoreCase);
        if ((!isMdd && !header.FilePath.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase)) ||
            header.GetAttribute("GeneratedByEngineVersion") != "2.0" ||
            (!isMdd && !header.GetAttribute("Encoding").Equals("UTF-8", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Only Oxford MDict v2.0 MDX (UTF-8) / MDD is supported.");
        if (!keys.AllKeysLoaded || keys.AllEntries.Count == 0)
            throw new ArgumentException("Read the complete Key index before the Record index.");

        using (FileStream stream = new FileStream(header.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            MdictBinary.CheckRange(keys.RecordSectionOffset, 32, stream.Length, "Record Section header");
            stream.Position = keys.RecordSectionOffset;
            RecordSectionInfo section = new RecordSectionInfo();
            section.BlockCount = MdictBinary.ReadInt64(reader);
            section.EntryCount = MdictBinary.ReadInt64(reader);
            section.RecordInfoSize = MdictBinary.ReadInt64(reader);
            section.RecordBlocksTotalSize = MdictBinary.ReadInt64(reader);

            if (section.BlockCount <= 0 || section.BlockCount > 1_000_000)
                throw new InvalidDataException("Unexpected Record Block count: " + section.BlockCount);
            if (section.EntryCount != keys.EntryCount)
                throw new InvalidDataException("Record Entry count does not match Key Entry count.");
            if (section.RecordInfoSize != checked(section.BlockCount * 16))
                throw new InvalidDataException("Record Block Info size does not equal BlockCount * 16.");
            long indexOffset = stream.Position;
            MdictBinary.CheckRange(indexOffset, section.RecordInfoSize, stream.Length, "Record Block Info");
            section.RecordBlocksFileOffset = checked(indexOffset + section.RecordInfoSize);
            MdictBinary.CheckRange(section.RecordBlocksFileOffset, section.RecordBlocksTotalSize,
                stream.Length, "Record Blocks");

            long filePosition = section.RecordBlocksFileOffset;
            long uncompressedPosition = 0;
            for (long i = 0; i < section.BlockCount; i++)
            {
                RecordBlockInfo block = new RecordBlockInfo();
                block.CompressedSize = MdictBinary.ReadInt64(reader);
                block.DecompressedSize = MdictBinary.ReadInt64(reader);
                block.FileOffset = filePosition;
                block.DecompressedStartOffset = uncompressedPosition;

                if (block.CompressedSize < 8 || block.CompressedSize > MaxPackedBlock ||
                    block.DecompressedSize <= 0 || block.DecompressedSize > MaxPlainBlock)
                    throw new InvalidDataException("Invalid Record Block lengths at index " + i);
                filePosition = checked(filePosition + block.CompressedSize);
                uncompressedPosition = checked(uncompressedPosition + block.DecompressedSize);
                section.Blocks.Add(block);
            }
            if (filePosition != checked(section.RecordBlocksFileOffset + section.RecordBlocksTotalSize))
                throw new InvalidDataException("Record Block compressed-size sum does not match its header.");
            section.TotalDecompressedSize = uncompressedPosition;
            if (keys.AllEntries[0].RecordOffset != 0)
                throw new InvalidDataException("First Key's record offset must be zero.");
            if (keys.AllEntries[keys.AllEntries.Count - 1].RecordOffset >= uncompressedPosition)
                throw new InvalidDataException("Last Key offset is outside Record Blocks.");
            return section;
        }
    }

    // Use a global Key index position rather than searching by text here. Duplicate
    // keys and multiple keys sharing an offset are both valid in this dictionary.
    public byte[] ReadRawRecord(string mdxPath, KeySectionInfo keys,
        RecordSectionInfo section, int keyIndex)
    {
        if (keyIndex < 0 || keyIndex >= keys.AllEntries.Count)
            throw new ArgumentOutOfRangeException(nameof(keyIndex));
        long start = keys.AllEntries[keyIndex].RecordOffset;
        long end = section.TotalDecompressedSize;
        // Skip equal start offsets: they point to the same record, not to empty records.
        for (int i = keyIndex + 1; i < keys.AllEntries.Count; i++)
        {
            if (keys.AllEntries[i].RecordOffset > start)
            {
                end = keys.AllEntries[i].RecordOffset;
                break;
            }
        }
        if (start < 0 || end < start || end > section.TotalDecompressedSize ||
            end - start > MaxSingleRecord)
            throw new InvalidDataException("Invalid or unexpectedly large record range: " +
                start + ".." + end);

        byte[] result = new byte[checked((int)(end - start))];
        int copied = 0;
        using (FileStream stream = new FileStream(mdxPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            for (int i = 0; i < section.Blocks.Count && copied < result.Length; i++)
            {
                RecordBlockInfo block = section.Blocks[i];
                long blockStart = block.DecompressedStartOffset;
                long blockEnd = checked(blockStart + block.DecompressedSize);
                if (blockEnd <= start || blockStart >= end)
                    continue;
                byte[] plain = ReadBlock(reader, block, i);
                long copyStart = Math.Max(start, blockStart);
                long copyEnd = Math.Min(end, blockEnd);
                int sourceOffset = checked((int)(copyStart - blockStart));
                int length = checked((int)(copyEnd - copyStart));
                Buffer.BlockCopy(plain, sourceOffset, result, copied, length);
                copied += length;
            }
        }
        if (copied != result.Length)
            throw new InvalidDataException("Record data was not completely copied from its blocks.");
        return result;
    }

    public string ReadHtml(string mdxPath, KeySectionInfo keys,
        RecordSectionInfo section, int keyIndex)
    {
        if (!mdxPath.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("MDD records are binary resources, not HTML.");
        byte[] raw = ReadRawRecord(mdxPath, keys, section, keyIndex);
        // MDict MDX records are generally terminated with zero bytes. Do not strip
        // HTML spaces or change markup, only remove trailing NUL bytes.
        int length = raw.Length;
        while (length > 0 && raw[length - 1] == 0)
            length--;
        return StrictUtf8.GetString(raw, 0, length);
    }

    private static byte[] ReadBlock(BinaryReader reader, RecordBlockInfo block, int index)
    {
        reader.BaseStream.Position = block.FileOffset;
        int packedLength = MdictBinary.ToArrayLength(block.CompressedSize,
            MaxPackedBlock, "Record Block " + index + " compressed");
        int unpackedLength = MdictBinary.ToArrayLength(block.DecompressedSize,
            MaxPlainBlock, "Record Block " + index + " uncompressed");
        byte[] packed = MdictBinary.ReadBytes(reader, packedLength);
        int compression = packed[0];
        if (packed[1] != 0 || packed[2] != 0 || packed[3] != 0)
            throw new InvalidDataException("Invalid compression marker in Record Block " + index);
        uint storedChecksum = MdictBinary.ToUInt32(packed, 4);
        byte[] plain;
        if (compression == 0)
        {
            if (packed.Length - 8 != unpackedLength)
                throw new InvalidDataException("Uncompressed Record Block length mismatch at " + index);
            plain = new byte[unpackedLength];
            Buffer.BlockCopy(packed, 8, plain, 0, plain.Length);
        }
        else if (compression == 2)
        {
            plain = new byte[unpackedLength];
            using (MemoryStream input = new MemoryStream(packed, 8, packed.Length - 8))
            using (ZLibStream decoder = new ZLibStream(input, CompressionMode.Decompress))
            {
                int position = 0;
                while (position < plain.Length)
                {
                    int read = decoder.Read(plain, position, plain.Length - position);
                    if (read == 0)
                        throw new InvalidDataException("Record Block " + index + " decompressed too short.");
                    position += read;
                }
                if (decoder.ReadByte() != -1)
                    throw new InvalidDataException("Record Block " + index + " decompressed too long.");
            }
        }
        else if (compression == 1)
            throw new NotSupportedException("Record Block " + index + " uses LZO (not supported).");
        else
            throw new NotSupportedException("Unknown Record Block compression at " + index + ": " + compression);

        uint actualChecksum = OxfordHeaderReader.CalculateAdler32(plain);
        if (storedChecksum != actualChecksum)
            throw new InvalidDataException("Record Block " + index + " Adler-32 mismatch.");
        return plain;
    }
}
}
