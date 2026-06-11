using Vintagestory.API.Client;

namespace VeinMiner
{
    // Minimal HUD that flashes the current mining mode above the hotbar and
    // disappears quickly — much snappier than the ingame discovery popup.
    public class VeinMinerModeHud : HudElement
    {
        private const int VisibleMs = 800;
        private long? closeCallbackId;

        public VeinMinerModeHud(ICoreClientAPI capi) : base(capi) { }

        public override bool Focusable => false;
        public override double DrawOrder => 0.95;

        public void Flash(string text)
        {
            var font = CairoFont.WhiteMediumText().WithOrientation(EnumTextOrientation.Center);

            var dialogBounds = new ElementBounds
            {
                Alignment = EnumDialogArea.CenterBottom,
                BothSizing = ElementSizing.Fixed,
                fixedWidth = 500,
                fixedHeight = 44,
                fixedOffsetY = -160,
            };

            SingleComposer?.Dispose();
            SingleComposer = capi.Gui.CreateCompo("veinminerrevamp-modehud", dialogBounds)
                .AddStaticText(text, font, ElementBounds.Fixed(0, 0, 500, 44))
                .Compose();

            TryOpen();

            if (closeCallbackId != null) capi.Event.UnregisterCallback(closeCallbackId.Value);
            closeCallbackId = capi.Event.RegisterCallback(_ =>
            {
                closeCallbackId = null;
                TryClose();
            }, VisibleMs);
        }
    }
}
