using System.Collections.Generic;
using ProtoBuf;

namespace VeinMiner
{
    [ProtoContract]
    public class VeinMinerConfig
    {
        [ProtoMember(1)] public int MaxBlocks { get; set; } = 64;

        // Empty default so protobuf-net doesn't append to a pre-populated list on deserialization.
        // The server sets the real default in StartServerSide when no config file exists.
        [ProtoMember(2)] public List<string> AllowedBlockPrefixes { get; set; } = new();

    }
}
