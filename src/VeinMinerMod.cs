using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

[assembly: Vintagestory.API.Common.ModInfo(
    name: "Vein Miner",
    modID: "veinminer",
    Version = "1.0.0",
    Description = "Hold sneak while breaking a block to mine the entire connected vein. Configurable block list.",
    Authors = new[] { "fuba" }
)]

namespace VeinMiner
{
    public class VeinMinerMod : ModSystem
    {
        private ICoreServerAPI? sapi;
        private VeinMinerConfig defaultConfig = new();
        private Dictionary<string, VeinMinerConfig> playerConfigs = new();
        private IServerNetworkChannel? channel;

        private readonly HashSet<BlockPos> breaking = new();

        // All 26 neighbors: faces, edges and corners
        private static readonly Vec3i[] Neighbors;
        static VeinMinerMod()
        {
            var list = new List<Vec3i>();
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                if (x == 0 && y == 0 && z == 0) continue;
                list.Add(new Vec3i(x, y, z));
            }
            Neighbors = list.ToArray();
        }

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            defaultConfig = api.LoadModConfig<VeinMinerConfig>("veinminer.json") ?? new VeinMinerConfig();
            api.StoreModConfig(defaultConfig, "veinminer.json");

            playerConfigs = api.LoadModConfig<Dictionary<string, VeinMinerConfig>>("veinminer-players.json") ?? new();

            channel = api.Network.RegisterChannel("veinminer")
                .RegisterMessageType<VeinMinerConfig>()
                .SetMessageHandler<VeinMinerConfig>(OnPlayerConfigReceived);

            api.Event.PlayerJoin += OnPlayerJoin;
            api.Event.DidBreakBlock += OnDidBreakBlock;
        }

        private void OnPlayerJoin(IServerPlayer player)
        {
            channel!.SendPacket(GetPlayerConfig(player.PlayerUID), player);
        }

        private void OnPlayerConfigReceived(IServerPlayer player, VeinMinerConfig packet)
        {
            playerConfigs[player.PlayerUID] = packet;
            sapi!.StoreModConfig(playerConfigs, "veinminer-players.json");
        }

        private VeinMinerConfig GetPlayerConfig(string uid)
            => playerConfigs.TryGetValue(uid, out var cfg) ? cfg : defaultConfig;

        private void OnDidBreakBlock(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel)
        {
            if (!byPlayer.Entity.Controls.Sneak) return;
            if (breaking.Contains(blockSel.Position)) return;

            Block brokenBlock = sapi!.World.GetBlock(oldblockId);
            if (brokenBlock?.Code == null) return;

            var cfg = GetPlayerConfig(byPlayer.PlayerUID);
            if (!IsAllowed(brokenBlock, cfg)) return;

            ItemSlot? toolSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            BlockPos dropOrigin = blockSel.Position.Copy();

            var toBreak = FindConnectedBlocks(blockSel.Position, brokenBlock.Code.ToString(), cfg.MaxBlocks);
            if (toBreak.Count == 0) return;

            foreach (BlockPos pos in toBreak)
            {
                breaking.Add(pos);
                try
                {
                    Block blockAtPos = sapi.World.BlockAccessor.GetBlock(pos);

                    // Collect drops directly into player inventory
                    ItemStack[] drops = blockAtPos.GetDrops(sapi.World, pos, byPlayer, 1f);
                    sapi.World.BlockAccessor.SetBlock(0, pos);
                    sapi.World.BlockAccessor.TriggerNeighbourBlockUpdate(pos);

                    if (drops != null)
                    {
                        foreach (ItemStack drop in drops)
                        {
                            if (drop == null) continue;
                            sapi.World.SpawnItemEntity(drop, dropOrigin, null);
                        }
                    }

                    if (toolSlot?.Itemstack != null)
                    {
                        toolSlot.Itemstack.Collectible.DamageItem(sapi.World, byPlayer.Entity, toolSlot, 1);
                        if (toolSlot.Itemstack == null) break;
                    }
                }
                finally
                {
                    breaking.Remove(pos);
                }
            }
        }

        private List<BlockPos> FindConnectedBlocks(BlockPos origin, string targetCode, int maxBlocks)
        {
            var result = new List<BlockPos>();
            var visited = new HashSet<BlockPos> { origin.Copy() };
            var queue = new Queue<BlockPos>();
            queue.Enqueue(origin.Copy());

            while (queue.Count > 0 && result.Count < maxBlocks)
            {
                BlockPos current = queue.Dequeue();

                foreach (Vec3i offset in Neighbors)
                {
                    BlockPos neighbor = current.AddCopy(offset.X, offset.Y, offset.Z);
                    if (visited.Contains(neighbor)) continue;
                    visited.Add(neighbor);

                    Block block = sapi!.World.BlockAccessor.GetBlock(neighbor);
                    if (block?.Code == null) continue;
                    if (block.Code.ToString() != targetCode) continue;

                    result.Add(neighbor);
                    queue.Enqueue(neighbor.Copy());
                }
            }

            return result;
        }

        private static bool IsAllowed(Block block, VeinMinerConfig cfg)
        {
            if (block.Code == null) return false;
            string code = block.Code.ToString();
            foreach (string prefix in cfg.AllowedBlockPrefixes)
                if (code.StartsWith(prefix)) return true;
            return false;
        }
    }
}
