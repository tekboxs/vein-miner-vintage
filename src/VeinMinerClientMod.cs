using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VeinMiner
{
    public class veinminerrevampClientMod : ModSystem
    {
        private ICoreClientAPI? capi;
        private VeinMinerGui? gui;
        private VeinMinerModeHud? modeHud;
        private IClientNetworkChannel? channel;
        private VeinMinerConfig currentConfig = new();

        private const int ModeCount = 6;

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            gui = new VeinMinerGui(api);
            gui.OnSaved = OnConfigSaved;
            modeHud = new VeinMinerModeHud(api);

            channel = api.Network.RegisterChannel("veinminerrevamp")
                .RegisterMessageType<VeinMinerConfig>()
                .SetMessageHandler<VeinMinerConfig>(OnConfigReceived);

            api.Input.RegisterHotKey(
                "VeinMinerGui",
                Lang.Get("veinminerrevamp:hotkey-name"),
                GlKeys.F7,
                HotkeyType.GUIOrOtherControls
            );
            api.Input.SetHotKeyHandler("VeinMinerGui", _ =>
            {
                if (gui.IsOpened())
                    gui.TryClose();
                else
                    gui.OpenWith(currentConfig);
                return true;
            });

            api.Input.RegisterHotKey(
                "VeinMinerCycleMode",
                Lang.Get("veinminerrevamp:hotkey-cycle-name"),
                GlKeys.V,
                HotkeyType.GUIOrOtherControls,
                shiftPressed: true
            );
            api.Input.SetHotKeyHandler("VeinMinerCycleMode", _ =>
            {
                CycleMode();
                return true;
            });

            api.Input.RegisterHotKey(
                "VeinMinerAddBlock",
                Lang.Get("veinminerrevamp:hotkey-addblock-name"),
                GlKeys.F7,
                HotkeyType.GUIOrOtherControls,
                shiftPressed: true
            );
            api.Input.SetHotKeyHandler("VeinMinerAddBlock", _ =>
            {
                ToggleTargetedBlock();
                return true;
            });
        }

        private void OnConfigReceived(VeinMinerConfig packet)
        {
            currentConfig = packet;
        }

        private void OnConfigSaved(VeinMinerConfig updated)
        {
            currentConfig = updated;
            channel!.SendPacket(updated);
        }

        // Shift+V: cycle through mining modes with a quick on-screen flash,
        // like the vanilla 'F' tool mode switcher.
        private void CycleMode()
        {
            MiningMode next;
            if (gui!.IsOpened())
            {
                // Only move the dropdown; the dialog saves (or cancels) it on close.
                next = gui.CycleModeSelection();
            }
            else
            {
                next = (MiningMode)(((int)currentConfig.Mode + 1) % ModeCount);
                currentConfig.Mode = next;
                channel!.SendPacket(currentConfig);
            }

            string name = VeinMinerGui.GetModeNames()[(int)next];
            modeHud!.Flash(Lang.Get("veinminerrevamp:mode-switched", name));
        }

        // Shift+F7: add/remove the block under the crosshair to the whitelist
        // without having to know its code.
        private void ToggleTargetedBlock()
        {
            if (gui!.IsOpened())
            {
                gui.TryAddTargetedBlock();
                return;
            }

            var sel = capi!.World.Player.CurrentBlockSelection;
            Block? block = sel == null ? null : capi.World.BlockAccessor.GetBlock(sel.Position);
            if (block?.Code == null || block.Id == 0)
            {
                capi.TriggerIngameError(this, "veinminerrevamp-notarget", Lang.Get("veinminerrevamp:no-block-targeted"));
                return;
            }

            string code = block.Code.ToString();
            string name;
            try { name = block.GetHeldItemName(new ItemStack(block)); }
            catch { name = code; }

            if (currentConfig.AllowedBlockPrefixes.Remove(code))
            {
                capi.TriggerIngameDiscovery(this, "veinminerrevamp-block", Lang.Get("veinminerrevamp:block-removed", name));
            }
            else
            {
                string? covering = currentConfig.AllowedBlockPrefixes.FirstOrDefault(p => code.StartsWith(p));
                if (covering != null)
                {
                    capi.TriggerIngameDiscovery(this, "veinminerrevamp-block", Lang.Get("veinminerrevamp:block-already", name, covering));
                    return;
                }
                currentConfig.AllowedBlockPrefixes.Add(code);
                capi.TriggerIngameDiscovery(this, "veinminerrevamp-block", Lang.Get("veinminerrevamp:block-added", name));
            }

            channel!.SendPacket(currentConfig);
        }
    }
}
