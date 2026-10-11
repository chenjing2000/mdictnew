using System.IO;
using System.IO.Compression;
using System.Text;
using Mdict.Core;

internal static class DictionaryFixture
{
    public static string Create(string directory, string name, string color, string exclusive)
    {
        string path = Path.Combine(directory, name + ".mdx");
        string[] words = new[] { "apple", "apple-pie", "appraise", "banana", "cooking-apple", exclusive }
            .Concat(Enumerable.Range(0, 55).Select(i => "term" + i.ToString("00"))).OrderBy(w => w, StringComparer.OrdinalIgnoreCase).ToArray();
        if (name == "B") words[Array.IndexOf(words, "apple")] = "APPLE";
        byte[][] records = words.Select(word => Encoding.UTF8.GetBytes(
            "<link rel=\"stylesheet\" href=\"shared.css\"><p class=\"marker\">" + name + " " + word + "</p>" +
            "<a href=\"entry://cooking-apple\">next</a><a href=\"#bottom\">anchor</a>" +
            "<div id=\"expansion\" style=\"height:900px\">body</div><p id=\"bottom\">bottom</p>\0")).ToArray();
        Write(path, false, words, records);
        Write(Path.ChangeExtension(path, ".mdd"), true, new[] { "\\shared.css", "\\sound\\shared.mp3" },
            new[] { Encoding.UTF8.GetBytes(".marker{color:" + color + "}"), Encoding.ASCII.GetBytes("ID3-" + name) });
        return path;
    }

    // Deterministic, one-block v2 MDX/MDD fixtures. Production readers parse the real bytes.
    private static void Write(string path, bool mdd, string[] keys, byte[][] records)
    {
        var keyEncoding = mdd ? Encoding.Unicode : Encoding.UTF8;
        using var keyBuffer = new MemoryStream();
        using (var keyWriter = new BinaryWriter(keyBuffer, Encoding.UTF8, true))
        {
            long offset = 0;
            for (int i = 0; i < keys.Length; i++)
            {
                U64(keyWriter, offset);
                keyWriter.Write(keyEncoding.GetBytes(keys[i]));
                keyWriter.Write(new byte[mdd ? 2 : 1]);
                offset += records[i].Length;
            }
        }
        byte[] keyData = keyBuffer.ToArray();
        byte[] packedKeys = Pack(keyData);
        using var infoBuffer = new MemoryStream();
        using (var infoWriter = new BinaryWriter(infoBuffer, Encoding.UTF8, true))
        {
            U64(infoWriter, keys.Length);
            foreach (string key in new[] { keys[0], keys[^1] })
            {
                int length = mdd ? key.Length : keyEncoding.GetByteCount(key);
                infoWriter.Write((byte)(length >> 8));
                infoWriter.Write((byte)length);
                infoWriter.Write(keyEncoding.GetBytes(key));
                infoWriter.Write(new byte[mdd ? 2 : 1]);
            }
            U64(infoWriter, packedKeys.Length);
            U64(infoWriter, keyData.Length);
        }
        byte[] info = infoBuffer.ToArray();
        byte[] packedInfo = Pack(info);
        using var file = new BinaryWriter(File.Create(path));
        string root = mdd ? "Library_Data" : "Dictionary";
        byte[] header = Encoding.Unicode.GetBytes("<" + root + " GeneratedByEngineVersion=\"2.0\" Encoding=\"UTF-8\" Encrypted=\"No\" />\r\n\0");
        U32(file, (uint)header.Length);
        file.Write(header);
        file.Write(OxfordHeaderReader.CalculateAdler32(header)); // Header checksum is little-endian.
        using var preambleBuffer = new MemoryStream();
        using (var preamble = new BinaryWriter(preambleBuffer, Encoding.UTF8, true))
        {
            U64(preamble, 1); U64(preamble, keys.Length); U64(preamble, info.Length);
            U64(preamble, packedInfo.Length); U64(preamble, packedKeys.Length);
        }
        byte[] preambleBytes = preambleBuffer.ToArray();
        file.Write(preambleBytes);
        U32(file, OxfordHeaderReader.CalculateAdler32(preambleBytes));
        file.Write(packedInfo);
        file.Write(packedKeys);
        byte[] data = records.SelectMany(r => r).ToArray();
        byte[] packedRecords = Pack(data);
        U64(file, 1); U64(file, keys.Length); U64(file, 16); U64(file, packedRecords.Length);
        U64(file, packedRecords.Length); U64(file, data.Length);
        file.Write(packedRecords);
    }

    private static byte[] Pack(byte[] data)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
        {
            writer.Write(2u);
            U32(writer, OxfordHeaderReader.CalculateAdler32(data));
        }
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Fastest, true)) zlib.Write(data);
        return buffer.ToArray();
    }

    private static void U32(BinaryWriter writer, uint value)
    {
        for (int i = 3; i >= 0; i--) writer.Write((byte)(value >> (i * 8)));
    }
    private static void U64(BinaryWriter writer, long value)
    {
        for (int i = 7; i >= 0; i--) writer.Write((byte)((ulong)value >> (i * 8)));
    }
}
