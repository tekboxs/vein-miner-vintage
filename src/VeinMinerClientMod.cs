using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VeinMiner
{
    public class VeinMinerClientMod : ModSystem
    {
        private ICoreClientAPI? capi;
        private VeinMinerGui? gui;
        private IClientNetworkChannel? channel;
        private VeinMinerConfig currentConfig = new();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            gui = new VeinMinerGui(api);
            gui.OnSaved = OnConfigSaved;

            channel = api.Network.RegisterChannel("veinminer")
                .RegisterMessageType<VeinMinerConfig>()
                .SetMessageHandler<VeinMinerConfig>(OnConfigReceived);

            api.Input.RegisterHotKey(
                "veinminergui",
                "Abrir VeinMiner Config",
                GlKeys.F7,
                HotkeyType.GUIOrOtherControls
            );
            api.Input.SetHotKeyHandler("veinminergui", _ =>
            {
                if (gui.IsOpened())
                    gui.TryClose();
                else
                    gui.OpenWith(currentConfig);
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
    }
}
