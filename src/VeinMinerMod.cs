using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

[assembly: Vintagestory.API.Common.ModInfo(
    name: "Vein Miner",
    modID: "veinminerrevamp",
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
            defaultConfig = api.LoadModConfig<VeinMinerConfig>("veinminerrevamp.json") ?? new VeinMinerConfig();
            if (defaultConfig.AllowedBlockPrefixes.Count == 0)
                defaultConfig.AllowedBlockPrefixes = new() { "game:ore-" };
            api.StoreModConfig(defaultConfig, "veinminerrevamp.json");

            playerConfigs = api.LoadModConfig<Dictionary<string, VeinMinerConfig>>("veinminerrevamp-players.json") ?? new();

            channel = api.Network.RegisterChannel("veinminerrevamp")
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
            sapi!.StoreModConfig(playerConfigs, "veinminerrevamp-players.json");
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
                int expansionRadius = Math.Clamp(cfg.ExpansionRadius, 0, 10);
                toBreak = FindConnectedBlocks(blockSel.Position, brokenBlock.Code.ToString(), cfg.MaxBlocks, expansionRadius);
            }
            else
            {
                toBreak = FindTunnelBlocks(blockSel, byPlayer, cfg);
            }

            if (toBreak.Count == 0) return;

            // All drops from the vein consolidate at the first broken block's position.
            Vec3d originDropPos = blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5);

            foreach (BlockPos pos in toBreak)
            {
                breaking.Add(pos);
                try
                {
                    Block blockAtPos = sapi!.World.BlockAccessor.GetBlock(pos);
                    if (blockAtPos == null || blockAtPos.Id == 0) continue;
                    if (blockAtPos.RequiredMiningTier > toolTier) continue;

                    // Snapshot item entities near this block before breaking so we can
                    // identify which ones were spawned by OnBlockBroken below.
                    Vec3d blockCenter = pos.ToVec3d().Add(0.5, 0.5, 0.5);
                    var entitiesBefore = new HashSet<long>();
                    foreach (var e in sapi.World.GetEntitiesAround(blockCenter, 1.5f, 1.5f, e => e is EntityItem))
                        entitiesBefore.Add(e.EntityId);

                    // OnBlockBroken runs the full drop pipeline (BlockBehaviors included),
                    // so ore blocks yield ore items and XSkills XP is granted via behaviors.
                    blockAtPos.OnBlockBroken(sapi.World, pos, byPlayer);

                    sapi.World.BlockAccessor.SetBlock(0, pos);
                    sapi.World.BlockAccessor.TriggerNeighbourBlockUpdate(pos);

                    // Teleport newly spawned drops to the origin so the entire vein's
                    // loot consolidates in one spot instead of scattering across the vein.
                    foreach (var e in sapi.World.GetEntitiesAround(blockCenter, 1.5f, 1.5f,
                        e => e is EntityItem && !entitiesBefore.Contains(e.EntityId)))
                    {
                        e.TeleportToDouble(originDropPos.X, originDropPos.Y, originDropPos.Z);
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

        // Tunnel modes: break a shaped tunnel in the player's facing direction.
        // MaxBlocks acts as tunnel depth (steps). Does not check block type prefixes.
        private List<BlockPos> FindTunnelBlocks(BlockSelection blockSel, IServerPlayer player, VeinMinerConfig cfg)
        {
            // The face the player hit is facing toward them, so its opposite is the dig direction.
            // For non-horizontal faces (e.g. player looks down at the floor), derive facing from yaw:
            // in VS, ViewVector = (sin(yaw), ..., cos(yaw)), so south=0, east=Ï€/2, north=Ï€, west=3Ï€/2.
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

        private List<BlockPos> FindConnectedBlocks(BlockPos origin, string targetCode, int maxBlocks, int expansionRadius)
        {
            var result = new List<BlockPos>();
            var visited = new HashSet<BlockPos> { origin.Copy() };
            var queue = new Queue<BlockPos>();
            queue.Enqueue(origin.Copy());

            while (result.Count < maxBlocks)
            {
                if (queue.Count == 0)
                {
                    // Chain exhausted: optionally bridge gaps by scanning outward
                    // from the last found block for more blocks of the same type.
                    BlockPos seed = result.Count > 0 ? result[result.Count - 1] : origin;
                    if (expansionRadius < 2 || !TryExpandSearch(seed, targetCode, expansionRadius, visited, queue, result, maxBlocks))
                        break;
                    continue;
                }

                BlockPos current = queue.Dequeue();

                foreach (Vec3i offset in Neighbors)
                {
                    if (result.Count >= maxBlocks) break;

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

        // Scans cube shells of growing radius around the seed (nearest first) and
        // seeds the BFS with every match found at the first non-empty distance.
        // Radius 1 is the BFS's own neighborhood, so shells start at 2.
        private bool TryExpandSearch(BlockPos seed, string targetCode, int radius, HashSet<BlockPos> visited, Queue<BlockPos> queue, List<BlockPos> result, int maxBlocks)
        {
            for (int r = 2; r <= radius; r++)
            {
                bool foundAny = false;

                for (int x = -r; x <= r; x++)
                for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                {
                    // Only the outer shell; inner cube was covered by smaller radii.
                    if (Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))) != r) continue;

                    BlockPos pos = seed.AddCopy(x, y, z);
                    if (visited.Contains(pos)) continue;

                    Block block = sapi!.World.BlockAccessor.GetBlock(pos);
                    if (block?.Code == null) continue;
                    if (block.Code.ToString() != targetCode) continue;

                    visited.Add(pos);
                    result.Add(pos);
                    queue.Enqueue(pos.Copy());
                    foundAny = true;
                    if (result.Count >= maxBlocks) return true;
                }

                if (foundAny) return true;
            }

            return false;
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
