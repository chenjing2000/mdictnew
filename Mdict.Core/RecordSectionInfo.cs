using System.Collections.Generic;

namespace Mdict.Core
{
public class RecordSectionInfo
{
    public long BlockCount { get; set; }
    public long EntryCount { get; set; }
    public long RecordInfoSize { get; set; }
    public long RecordBlocksTotalSize { get; set; }
    public long RecordBlocksFileOffset { get; set; }
    public long TotalDecompressedSize { get; set; }
    public List<RecordBlockInfo> Blocks { get; } = new List<RecordBlockInfo>();
}
}
