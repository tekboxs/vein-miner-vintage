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

        private static readonly string[] ModeValues = { "0", "1", "2", "3", "4", "5" };

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
            string modeLabelTxt = Lang.Get("veinminer:gui-mode-label");
            string saveText     = Lang.Get("veinminer:gui-save");
            string closeText    = Lang.Get("veinminer:gui-close");

            string[] modeNames =
            {
                Lang.Get("veinminer:gui-mode-vein"),
                Lang.Get("veinminer:gui-mode-tunnel1x1"),
                Lang.Get("veinminer:gui-mode-tunnel1x2"),
                Lang.Get("veinminer:gui-mode-tunnel3x3"),
                Lang.Get("veinminer:gui-mode-miningtunnel"),
                Lang.Get("veinminer:gui-mode-escapetunnel"),
            };

            double titleH = GuiStyle.TitleBarHeight;
            double y = titleH + 16;

            // Row 1: Maximum blocks label (left) + number input (right)
            var maxLabel = ElementBounds.Fixed(0,       y + 4, 260, 24);
            var maxInput = ElementBounds.Fixed(cw - 80, y,     80,  28);
            y += 44;

            // Row 2: Mining mode label (left) + dropdown (right)
            var modeLabel    = ElementBounds.Fixed(0,       y + 4, 200, 24);
            var modeDropdown = ElementBounds.Fixed(cw - 200, y,    200,  28);
            y += 44;

            // Row 3: Prefix textarea label
            var prefixLabel = ElementBounds.Fixed(0, y, cw, 24);
            y += 30;

            // Row 4: Textarea
            var textArea = ElementBounds.Fixed(0, y, cw, 200);
            y += 210;

            // Row 5: Save / Close buttons flush-right
            var saveBtn  = ElementBounds.Fixed(cw - 188, y, 90, 28);
            var closeBtn = ElementBounds.Fixed(cw - 90,  y, 90, 28);

            var bgBounds = ElementBounds.Fixed(0, 0, cw + pad * 2, y + 28 + pad + titleH)
                .WithFixedPadding(pad);

            var dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle);

            string prefixText = string.Join("\n", config.AllowedBlockPrefixes);
            int selectedMode = (int)config.Mode;

            SingleComposer?.Dispose();

            SingleComposer = capi.Gui.CreateCompo("veinminercfg", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(titleText, () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText(maxBlocksTxt, labelFont, maxLabel)
                    .AddNumberInput(maxInput, _ => { }, font, "maxblocks")
                    .AddStaticText(modeLabelTxt, labelFont, modeLabel)
                    .AddDropDown(ModeValues, modeNames, selectedMode, (_, _) => { }, modeDropdown, font, "mode")
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

            string modeStr = SingleComposer.GetDropDown("mode").SelectedValue;
            MiningMode mode = int.TryParse(modeStr, out int modeInt)
                ? (MiningMode)modeInt
                : MiningMode.Vein;

            config.AllowedBlockPrefixes = prefixes;
            config.MaxBlocks = Math.Clamp((int)maxVal, 1, 512);
            config.Mode = mode;
            OnSaved?.Invoke(config);
            TryClose();
            return true;
        }

        public override bool DisableMouseGrab => true;
    }
}
