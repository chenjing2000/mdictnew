namespace Mdict.Core
{
// One compressed Record Block on disk, and its range in the uncompressed record stream.
public class RecordBlockInfo
{
    public long FileOffset { get; set; }
    public long CompressedSize { get; set; }
    public long DecompressedSize { get; set; }
    public long DecompressedStartOffset { get; set; }
}
}
