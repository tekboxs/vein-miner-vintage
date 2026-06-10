using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;

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
            double cw = 380; // content width

            var font = CairoFont.WhiteSmallText();

            // All bounds relative to BeginChildElements area
            var prefixLabel  = ElementBounds.Fixed(0,   0,   cw,  22);
            var textArea     = ElementBounds.Fixed(0,   27,  cw,  150);
            var maxLabel     = ElementBounds.Fixed(0,   192, 240, 22);
            var maxInput     = ElementBounds.Fixed(250, 188, 100, 30);
            var saveBtn      = ElementBounds.Fixed(cw - 175, 232, 80, 25);
            var closeBtn     = ElementBounds.Fixed(cw - 85,  232, 80, 25);

            var bgBounds = ElementBounds.Fill.WithFixedPadding(pad);
            bgBounds.BothSizing = ElementSizing.FitToChildren;

            var dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle);

            string prefixText = string.Join("\n", config.AllowedBlockPrefixes);

            SingleComposer?.Dispose();

            SingleComposer = capi.Gui.CreateCompo("veinminercfg", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("VeinMiner — Configurações", () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText("Prefixos de blocos permitidos (um por linha):", font, prefixLabel)
                    .AddTextArea(textArea, _ => { }, font, "prefixes")
                    .AddStaticText("Máximo de blocos:", font, maxLabel)
                    .AddNumberInput(maxInput, _ => { }, font, "maxblocks")
                    .AddSmallButton("Salvar",  OnSaveClick,           saveBtn)
                    .AddSmallButton("Fechar",  () => { TryClose(); return true; }, closeBtn)
                .EndChildElements()
                .Compose();

            SingleComposer.GetTextArea("prefixes").SetValue(prefixText);
            SingleComposer.GetNumberInput("maxblocks").SetValue(config.MaxBlocks.ToString());
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
