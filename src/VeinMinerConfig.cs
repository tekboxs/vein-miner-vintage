using System.Collections.Generic;
using ProtoBuf;

namespace VeinMiner
{
    [ProtoContract]
    public class VeinMinerConfig
    {
        [ProtoMember(1)] public int MaxBlocks { get; set; } = 64;

        [ProtoMember(2)] public List<string> AllowedBlockPrefixes { get; set; } = new()
        {
            "game:ore-"
        };

    }
}
