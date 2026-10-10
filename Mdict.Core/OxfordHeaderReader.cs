using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Mdict.Core
{

// v0.1: inspect the file header only. Does not parse word or resource data.
public class OxfordHeaderReader
{
    // A header is normally small. This limit prevents allocating arbitrary data
    // when an invalid file claims an unreasonable length.
    private const int MaxHeaderBytes = 16 * 1024 * 1024;

    public MdictHeader Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be empty.");
        }

        string fullPath = Path.GetFullPath(filePath);
        string extension = Path.GetExtension(fullPath);

        if (!extension.Equals(".mdx", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".mdd", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Expected a .mdx or .mdd file.");
        }

        using (FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (BinaryReader reader = new BinaryReader(stream))
        {
            long fileLength = stream.Length;
            if (fileLength < 10)
            {
                throw new InvalidDataException("File is too short to contain an MDict header.");
            }

            // First 4 bytes: unsigned header byte count, big-endian.
            byte[] sizeBytes = ReadExactly(reader, 4);
            uint byteCount = ((uint)sizeBytes[0] << 24) |
                             ((uint)sizeBytes[1] << 16) |
                             ((uint)sizeBytes[2] << 8) |
                             sizeBytes[3];

            if (byteCount < 2 || byteCount % 2 != 0 || byteCount > MaxHeaderBytes)
            {
                throw new InvalidDataException("Invalid UTF-16LE header byte count: " + byteCount);
            }

            if ((long)byteCount > fileLength - 8)
            {
                throw new InvalidDataException("Header extends beyond the end of the file.");
            }

            byte[] headerBytes = ReadExactly(reader, (int)byteCount);

            // Next 4 bytes: Adler-32 of the *raw header bytes*, little-endian.
            byte[] checksumBytes = ReadExactly(reader, 4);
            uint storedChecksum = (uint)checksumBytes[0] |
                                  ((uint)checksumBytes[1] << 8) |
                                  ((uint)checksumBytes[2] << 16) |
                                  ((uint)checksumBytes[3] << 24);
            uint computedChecksum = CalculateAdler32(headerBytes);

            if (storedChecksum != computedChecksum)
            {
                throw new InvalidDataException(
                    "Header Adler-32 mismatch. Stored: 0x" + storedChecksum.ToString("X8") +
                    ", computed: 0x" + computedChecksum.ToString("X8") + ".");
            }

            UnicodeEncoding strictUtf16Le = new UnicodeEncoding(false, false, true);
            string headerText = strictUtf16Le.GetString(headerBytes);
            headerText = headerText.TrimStart('\uFEFF').TrimEnd('\0');

            // Some real MDict files contain imperfect XML in Description.
            // Extract only double-quoted header attributes; don't depend on
            // the entire header being valid XML.
            Match rootMatch = Regex.Match(headerText, @"<\s*([A-Za-z_][A-Za-z0-9_]*)\b");
            if (!rootMatch.Success)
            {
                throw new InvalidDataException("MDict header root element was not found.");
            }

            string rootName = rootMatch.Groups[1].Value;
            if (rootName != "Dictionary" && rootName != "Library_Data")
            {
                throw new InvalidDataException("Unexpected MDict header root: " + rootName);
            }

            MdictHeader header = new MdictHeader();
            header.FilePath = fullPath;
            header.FileLength = fileLength;
            header.HeaderByteCount = byteCount;
            header.KeySectionOffset = stream.Position;
            header.RootName = rootName;
            header.StoredChecksum = storedChecksum;
            header.ComputedChecksum = computedChecksum;

            MatchCollection matches = Regex.Matches(
                headerText,
                @"([A-Za-z_][A-Za-z0-9_]*)\s*=\s*""([^""]*)""",
                RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                string name = match.Groups[1].Value;
                string value = WebUtility.HtmlDecode(match.Groups[2].Value);
                header.Attributes[name] = value;
            }

            if (header.Attributes.Count == 0)
            {
                throw new InvalidDataException("No attributes were found in the MDict header.");
            }

            return header;
        }
    }

    private static byte[] ReadExactly(BinaryReader reader, int count)
    {
        byte[] data = reader.ReadBytes(count);
        if (data.Length != count)
        {
            throw new InvalidDataException("Unexpected end of file.");
        }
        return data;
    }

    public static uint CalculateAdler32(byte[] data)
    {
        const uint mod = 65521;
        uint a = 1;
        uint b = 0;

        for (int i = 0; i < data.Length; i++)
        {
            a = (a + data[i]) % mod;
            b = (b + a) % mod;
        }

        return (b << 16) | a;
    }
}
}
