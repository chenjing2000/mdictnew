namespace Mdict.Core
{
public class KeyBlockInfo
{
    public long EntryCount { get; set; }
    public string FirstKey { get; set; } = "";
    public string LastKey { get; set; } = "";
    public long CompressedSize { get; set; }
    public long DecompressedSize { get; set; }
    public long FileOffset { get; set; }
}
}
