using System.Collections.Generic;
using ProtoBuf;

namespace VeinMiner
{
    public enum MiningMode
    {
        Vein         = 0,
        Tunnel1x1    = 1,
        Tunnel1x2    = 2,
        Tunnel3x3    = 3,
        MiningTunnel = 4,
        EscapeTunnel = 5,
    }

    [ProtoContract]
    public class VeinMinerConfig
    {
        [ProtoMember(1)] public int MaxBlocks { get; set; } = 64;

        // Empty default so protobuf-net doesn't append to a pre-populated list on deserialization.
        // The server sets the real default in StartServerSide when no config file exists.
        [ProtoMember(2)] public List<string> AllowedBlockPrefixes { get; set; } = new();

        [ProtoMember(3)] public MiningMode Mode { get; set; } = MiningMode.Vein;
    }
}
