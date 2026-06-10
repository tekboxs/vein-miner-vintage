using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

[assembly: Vintagestory.API.Common.ModInfo(
    name: "Vein Miner",
    modID: "veinminer",
    Version = "1.2.0",
    Description = "Hold sneak while breaking a block to activate the selected mining mode. Configurable via F7.",
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
            if (defaultConfig.AllowedBlockPrefixes.Count == 0)
                defaultConfig.AllowedBlockPrefixes = new() { "game:ore-" };
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

            var cfg = GetPlayerConfig(byPlayer.PlayerUID);

            List<BlockPos> toBreak;

            ItemSlot? toolSlot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            int toolTier = toolSlot?.Itemstack?.Collectible?.ToolTier ?? 0;

            if (cfg.Mode == MiningMode.Vein)
            {
                Block brokenBlock = sapi!.World.GetBlock(oldblockId);
                if (brokenBlock?.Code == null) return;
                if (!IsAllowed(brokenBlock, cfg)) return;
                toBreak = FindConnectedBlocks(blockSel.Position, brokenBlock.Code.ToString(), cfg.MaxBlocks);
            }
            else
            {
                toBreak = FindTunnelBlocks(blockSel, byPlayer, cfg);
            }

            if (toBreak.Count == 0) return;

            foreach (BlockPos pos in toBreak)
            {
                breaking.Add(pos);
                try
                {
                    Block blockAtPos = sapi!.World.BlockAccessor.GetBlock(pos);
                    if (blockAtPos == null || blockAtPos.Id == 0) continue;
                    if (blockAtPos.RequiredMiningTier > toolTier) continue;

                    // OnBlockBroken runs the full drop pipeline (BlockBehaviors included),
                    // so ore blocks yield ore items instead of the raw block.
                    blockAtPos.OnBlockBroken(sapi.World, pos, byPlayer);

                    sapi.World.BlockAccessor.SetBlock(0, pos);
                    sapi.World.BlockAccessor.TriggerNeighbourBlockUpdate(pos);

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

        // Tunnel modes: break a shaped tunnel in the player's facing direction.
        // MaxBlocks acts as tunnel depth (steps). Does not check block type prefixes.
        private List<BlockPos> FindTunnelBlocks(BlockSelection blockSel, IServerPlayer player, VeinMinerConfig cfg)
        {
            // The face the player hit is facing toward them, so its opposite is the dig direction.
            // For non-horizontal faces (e.g. player looks down at the floor), derive facing from yaw:
            // in VS, ViewVector = (sin(yaw), ..., cos(yaw)), so south=0, east=π/2, north=π, west=3π/2.
            BlockFacing facing;
            if (blockSel.Face.IsHorizontal)
            {
                facing = blockSel.Face.Opposite;
            }
            else
            {
                float yaw = player.Entity.Pos.Yaw;
                double fx = Math.Sin(yaw);
                double fz = Math.Cos(yaw);
                if (Math.Abs(fx) >= Math.Abs(fz))
                    facing = fx > 0 ? BlockFacing.EAST : BlockFacing.WEST;
                else
                    facing = fz > 0 ? BlockFacing.SOUTH : BlockFacing.NORTH;
            }

            BlockPos origin = blockSel.Position;
            int dx = facing.Normali.X;
            int dz = facing.Normali.Z;

            var result = new List<BlockPos>();
            int depth = cfg.MaxBlocks;

            switch (cfg.Mode)
            {
                case MiningMode.Tunnel1x1:
                    for (int i = 0; i < depth; i++)
                        TryAdd(origin.AddCopy(dx * i, 0, dz * i), result);
                    break;

                case MiningMode.Tunnel1x2:
                    for (int i = 0; i < depth; i++)
                    {
                        TryAdd(origin.AddCopy(dx * i, 0, dz * i), result);
                        TryAdd(origin.AddCopy(dx * i, 1, dz * i), result);
                    }
                    break;

                case MiningMode.Tunnel3x3:
                {
                    // perpendicular to facing in the horizontal plane
                    int px = dz, pz = -dx;
                    for (int i = 0; i < depth; i++)
                        for (int p = -1; p <= 1; p++)
                            for (int h = -1; h <= 1; h++)
                                TryAdd(origin.AddCopy(dx * i + px * p, h, dz * i + pz * p), result);
                    break;
                }

                case MiningMode.MiningTunnel:
                    // Staircase going down: each step descends 1 block.
                    // Break head-level (Y-i) and feet-level (Y-i-1) so the 2-block clearance is below origin.
                    for (int i = 0; i < depth; i++)
                    {
                        TryAdd(origin.AddCopy(dx * i, -i,     dz * i), result);
                        TryAdd(origin.AddCopy(dx * i, -i - 1, dz * i), result);
                    }
                    break;

                case MiningMode.EscapeTunnel:
                    // Staircase going up: each step rises 1 block.
                    // Break feet-level (Y+i-1) and head-level (Y+i) so clearance is centered on the climb.
                    for (int i = 0; i < depth; i++)
                    {
                        TryAdd(origin.AddCopy(dx * i, i - 1, dz * i), result);
                        TryAdd(origin.AddCopy(dx * i, i,     dz * i), result);
                    }
                    break;
            }

            return result;
        }

        private void TryAdd(BlockPos pos, List<BlockPos> result)
        {
            Block block = sapi!.World.BlockAccessor.GetBlock(pos);
            if (block != null && block.Id != 0)
                result.Add(pos.Copy());
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
