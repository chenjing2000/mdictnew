using System;
using System.Collections.Generic;

namespace Mdict.Core
{

// Only the information verified from the MDX/MDD file header.
public class MdictHeader
{
    public string FilePath { get; set; } = "";
    public long FileLength { get; set; }
    public long HeaderByteCount { get; set; }
    public long KeySectionOffset { get; set; }
    public string RootName { get; set; } = "";
    public uint StoredChecksum { get; set; }
    public uint ComputedChecksum { get; set; }
    public Dictionary<string, string> Attributes { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string GetAttribute(string name)
    {
        string value;
        if (Attributes.TryGetValue(name, out value))
        {
            return value;
        }

        return "(not specified)";
    }
}
}
