using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VeinMiner
{
    public class VeinMinerGui : GuiDialog
    {
        public override string ToggleKeyCombinationCode => "veinminergui";

        private VeinMinerConfig config = new();
        public Action<VeinMinerConfig>? OnSaved;

        public VeinMinerGui(ICoreClientAPI capi) : base(capi) { }

        public void OpenWith(VeinMinerConfig cfg)
        {
            config = cfg;
            ComposeDialog();
            TryOpen();
        }

        private void ComposeDialog()
        {
            double pad = GuiStyle.ElementToDialogPadding;
            double cw  = 420;
            var font      = CairoFont.WhiteSmallText();
            var labelFont = CairoFont.WhiteSmallText();

            string titleText    = Lang.Get("veinminer:gui-title");
            string prefixesText = Lang.Get("veinminer:gui-prefixes-label");
            string maxBlocksTxt = Lang.Get("veinminer:gui-maxblocks-label");
            string saveText     = Lang.Get("veinminer:gui-save");
            string closeText    = Lang.Get("veinminer:gui-close");

            double titleH = GuiStyle.TitleBarHeight;
            double y = titleH + 16;

            // Row 1: "Maximum blocks:" label on the left, number input on the right
            var maxLabel = ElementBounds.Fixed(0,       y + 4, 260, 24);
            var maxInput = ElementBounds.Fixed(cw - 80, y,     80,  28);
            y += 44;

            // Row 2: Separator / prefix label
            var prefixLabel = ElementBounds.Fixed(0, y, cw, 24);
            y += 30;

            // Row 3: Textarea — tall enough to show several entries without overflow
            var textArea = ElementBounds.Fixed(0, y, cw, 220);
            y += 230;

            // Row 4: Save / Close buttons flush-right
            var closeBtn = ElementBounds.Fixed(cw - 90,  y, 90, 28);
            var saveBtn  = ElementBounds.Fixed(cw - 188, y, 90, 28);

            var bgBounds = ElementBounds.Fixed(0, 0, cw + pad * 2, y + 28 + pad + titleH)
                .WithFixedPadding(pad);

            var dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle);

            string prefixText = string.Join("\n", config.AllowedBlockPrefixes);

            SingleComposer?.Dispose();

            SingleComposer = capi.Gui.CreateCompo("veinminercfg", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(titleText, () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText(maxBlocksTxt, labelFont, maxLabel)
                    .AddNumberInput(maxInput, _ => { }, font, "maxblocks")
                    .AddStaticText(prefixesText, labelFont, prefixLabel)
                    .AddTextArea(textArea, _ => { }, font, "prefixes")
                    .AddSmallButton(saveText,  OnSaveClick, saveBtn)
                    .AddSmallButton(closeText, () => { TryClose(); return true; }, closeBtn)
                .EndChildElements()
                .Compose();

            SingleComposer.GetTextArea("prefixes").SetValue(prefixText, true);
            SingleComposer.GetNumberInput("maxblocks").SetValue((float)config.MaxBlocks);
        }

        private bool OnSaveClick()
        {
            string raw = SingleComposer.GetTextArea("prefixes").GetText();
            List<string> prefixes = raw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            float maxVal = SingleComposer.GetNumberInput("maxblocks").GetValue();

            config.AllowedBlockPrefixes = prefixes;
            config.MaxBlocks = Math.Clamp((int)maxVal, 1, 512);
            OnSaved?.Invoke(config);
            TryClose();
            return true;
        }

        public override bool DisableMouseGrab => true;
    }
}
