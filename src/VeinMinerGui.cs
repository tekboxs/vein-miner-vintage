using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VeinMiner
{
    public class VeinMinerGui : GuiDialog
    {
        public override string ToggleKeyCombinationCode => "VeinMinerGui";

        private VeinMinerConfig config = new();
        public Action<VeinMinerConfig>? OnSaved;

        private Block? targetedBlock;
        private bool discardChanges;

        private const double TextAreaHeight = 150;
        private const int ModeCount = 6;

        private static readonly string[] ModeValues = { "0", "1", "2", "3", "4", "5" };

        public VeinMinerGui(ICoreClientAPI capi) : base(capi) { }

        public static string[] GetModeNames() => new[]
        {
            Lang.Get("veinminerrevamp:gui-mode-vein"),
            Lang.Get("veinminerrevamp:gui-mode-tunnel1x1"),
            Lang.Get("veinminerrevamp:gui-mode-tunnel1x2"),
            Lang.Get("veinminerrevamp:gui-mode-tunnel3x3"),
            Lang.Get("veinminerrevamp:gui-mode-miningtunnel"),
            Lang.Get("veinminerrevamp:gui-mode-escapetunnel"),
        };

        public void OpenWith(VeinMinerConfig cfg)
        {
            config = cfg;
            discardChanges = false;
            targetedBlock = GetTargetedBlock();
            ComposeDialog();
            TryOpen();
        }

        private Block? GetTargetedBlock()
        {
            var sel = capi.World?.Player?.CurrentBlockSelection;
            if (sel == null) return null;
            Block block = capi.World!.BlockAccessor.GetBlock(sel.Position);
            return block == null || block.Id == 0 || block.Code == null ? null : block;
        }

        private static string BlockDisplayName(Block block)
        {
            try
            {
                return block.GetHeldItemName(new ItemStack(block));
            }
            catch
            {
                return block.Code.ToString();
            }
        }

        private void ComposeDialog()
        {
            double pad = GuiStyle.ElementToDialogPadding;
            double cw  = 420;
            var font       = CairoFont.WhiteSmallText();
            var labelFont  = CairoFont.WhiteSmallText();
            var hintFont   = CairoFont.WhiteDetailText();

            string titleText    = Lang.Get("veinminerrevamp:gui-title");
            string prefixesText = Lang.Get("veinminerrevamp:gui-prefixes-label");
            string maxBlocksTxt = Lang.Get("veinminerrevamp:gui-maxblocks-label");
            string expRadiusTxt = Lang.Get("veinminerrevamp:gui-expradius-label");
            string modeLabelTxt = Lang.Get("veinminerrevamp:gui-mode-label");
            string saveText     = Lang.Get("veinminerrevamp:gui-save");
            string cancelText   = Lang.Get("veinminerrevamp:gui-cancel");

            string[] modeNames = GetModeNames();

            double titleH = GuiStyle.TitleBarHeight;
            double y = titleH + 16;

            // Row 1: Maximum blocks label (left) + number input (right)
            var maxLabel = ElementBounds.Fixed(0,        y + 4, 260, 24);
            var maxInput = ElementBounds.Fixed(cw - 100, y,     100, 28);
            y += 40;

            // Row 1b: Expansion radius label (left) + number input (right)
            var expLabel = ElementBounds.Fixed(0,        y + 4, 300, 24);
            var expInput = ElementBounds.Fixed(cw - 100, y,     100, 28);
            y += 40;

            // Row 2: Mining mode label (left) + dropdown (right)
            var modeLabel    = ElementBounds.Fixed(0,        y + 4, 200, 24);
            var modeDropdown = ElementBounds.Fixed(cw - 200, y,     200, 28);
            y += 38;

            // Row 3: hotkey hint
            var hotkeyHint = ElementBounds.Fixed(0, y, cw, 22);
            y += 22;

            // Row 3b: vertical-digging hint (tunnel modes only)
            var verticalHint = ElementBounds.Fixed(0, y, cw, 22);
            y += 30;

            // Row 4: Prefix textarea label
            var prefixLabel = ElementBounds.Fixed(0, y, cw, 24);
            y += 28;

            // Row 5: "add targeted block" button, or a tip when nothing was targeted
            ElementBounds addRow = ElementBounds.Fixed(0, y, cw, 28);
            y += 38;

            // Row 6: Textarea inside a clipped, scrollable region so long lists
            // can't grow over the buttons below.
            var textAreaBounds = ElementBounds.Fixed(0, 0, cw - 26, TextAreaHeight);
            var clipBounds     = textAreaBounds.ForkBoundingParent().WithFixedPosition(0, y);
            var insetBounds    = ElementBounds.Fixed(-3, y - 3, cw - 20, TextAreaHeight + 6);
            var scrollbar      = ElementBounds.Fixed(cw - 20, y, 20, TextAreaHeight);
            y += TextAreaHeight + 14;

            // Row 7: autosave hint
            var saveHint = ElementBounds.Fixed(0, y, cw, 22);
            y += 28;

            // Row 8: Cancel / Save buttons flush-right
            var cancelBtn = ElementBounds.Fixed(cw - 210, y, 100, 28);
            var saveBtn   = ElementBounds.Fixed(cw - 100, y, 100, 28);

            var bgBounds = ElementBounds.Fixed(0, 0, cw + pad * 2, y + 28 + pad + titleH)
                .WithFixedPadding(pad);

            var dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.CenterMiddle);

            string prefixText = string.Join("\n", config.AllowedBlockPrefixes);
            int selectedMode = (int)config.Mode;

            SingleComposer?.Dispose();

            var composer = capi.Gui.CreateCompo("veinminerrevampcfg", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(titleText, () => TryClose())
                .BeginChildElements(bgBounds)
                    .AddStaticText(maxBlocksTxt, labelFont, maxLabel)
                    .AddNumberInput(maxInput, _ => { }, font, "maxblocks")
                    .AddStaticText(expRadiusTxt, labelFont, expLabel)
                    .AddNumberInput(expInput, _ => { }, font, "expradius")
                    .AddStaticText(modeLabelTxt, labelFont, modeLabel)
                    .AddDropDown(ModeValues, modeNames, selectedMode, (_, _) => { }, modeDropdown, font, "mode")
                    .AddStaticText(Lang.Get("veinminerrevamp:gui-hotkeys-hint"), hintFont, hotkeyHint)
                    .AddStaticText(Lang.Get("veinminerrevamp:gui-vertical-hint"), hintFont, verticalHint)
                    .AddStaticText(prefixesText, labelFont, prefixLabel);

            if (targetedBlock != null)
            {
                composer.AddSmallButton(
                    Lang.Get("veinminerrevamp:gui-add-targeted", BlockDisplayName(targetedBlock)),
                    OnAddTargetedClick, addRow);
            }
            else
            {
                composer.AddStaticText(Lang.Get("veinminerrevamp:gui-no-target-hint"), hintFont, addRow);
            }

            SingleComposer = composer
                    .AddInset(insetBounds, 3)
                    .BeginClip(clipBounds)
                        .AddTextArea(textAreaBounds, OnPrefixesChanged, font, "prefixes")
                    .EndClip()
                    .AddVerticalScrollbar(OnNewScrollbarValue, scrollbar, "scrollbar")
                    .AddStaticText(Lang.Get("veinminerrevamp:gui-autosave-hint"), hintFont, saveHint)
                    .AddSmallButton(cancelText, OnCancelClick, cancelBtn)
                    .AddSmallButton(saveText, OnSaveClick, saveBtn)
                .EndChildElements()
                .Compose();

            SingleComposer.GetTextArea("prefixes").SetValue(prefixText, true);
            SingleComposer.GetNumberInput("maxblocks").SetValue((float)config.MaxBlocks);
            SingleComposer.GetNumberInput("expradius").SetValue((float)config.ExpansionRadius);
            UpdateScrollbarHeights();
        }

        private void OnPrefixesChanged(string _)
        {
            UpdateScrollbarHeights();
        }

        private void UpdateScrollbarHeights()
        {
            var ta = SingleComposer?.GetTextArea("prefixes");
            var sb = SingleComposer?.GetScrollbar("scrollbar");
            if (ta == null || sb == null) return;
            sb.SetHeights((float)TextAreaHeight, (float)Math.Max(TextAreaHeight, ta.Bounds.fixedHeight));
        }

        private void OnNewScrollbarValue(float value)
        {
            var ta = SingleComposer?.GetTextArea("prefixes");
            if (ta == null) return;
            ta.Bounds.fixedY = -value;
            ta.Bounds.CalcWorldBounds();
        }

        private bool OnAddTargetedClick()
        {
            TryAddTargetedBlock();
            return true;
        }

        // Also called by the Shift+F7 hotkey while the dialog is open, so the
        // pending (unsaved) textarea content stays the single source of truth.
        public void TryAddTargetedBlock()
        {
            targetedBlock ??= GetTargetedBlock();
            if (targetedBlock?.Code == null)
            {
                capi.TriggerIngameError(this, "veinminerrevamp-notarget", Lang.Get("veinminerrevamp:no-block-targeted"));
                return;
            }

            string code = targetedBlock.Code.ToString();
            string name = BlockDisplayName(targetedBlock);

            var ta = SingleComposer?.GetTextArea("prefixes");
            if (ta == null) return;

            var lines = ta.GetText()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            string? covering = lines.FirstOrDefault(p => code.StartsWith(p));
            if (covering != null)
            {
                capi.TriggerIngameError(this, "veinminerrevamp-listed", Lang.Get("veinminerrevamp:block-already", name, covering));
                return;
            }

            lines.Add(code);
            ta.SetValue(string.Join("\n", lines), true);
            UpdateScrollbarHeights();
        }

        // Lets the Ctrl+F7 hotkey cycle the dropdown while the dialog is open,
        // without touching the live config (Cancel must still discard it).
        public MiningMode CycleModeSelection()
        {
            var dd = SingleComposer?.GetDropDown("mode");
            int current = dd != null && int.TryParse(dd.SelectedValue, out int idx) ? idx : 0;
            int next = (current + 1) % ModeCount;
            dd?.SetSelectedIndex(next);
            return (MiningMode)next;
        }

        private bool OnSaveClick()
        {
            TryClose(); // OnGuiClosed applies the changes
            return true;
        }

        private bool OnCancelClick()
        {
            discardChanges = true;
            TryClose();
            return true;
        }

        // Closing by any means (X, Esc, F7, Save) saves automatically;
        // only the Cancel button discards.
        public override void OnGuiClosed()
        {
            if (discardChanges) discardChanges = false;
            else ApplyChanges();
            base.OnGuiClosed();
        }

        private void ApplyChanges()
        {
            if (SingleComposer == null) return;

            string raw = SingleComposer.GetTextArea("prefixes").GetText();
            List<string> prefixes = raw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            float maxVal = SingleComposer.GetNumberInput("maxblocks").GetValue();
            int maxBlocks = float.IsNaN(maxVal) ? config.MaxBlocks : (int)maxVal;

            float expVal = SingleComposer.GetNumberInput("expradius").GetValue();
            int expRadius = float.IsNaN(expVal) ? config.ExpansionRadius : (int)expVal;

            string modeStr = SingleComposer.GetDropDown("mode").SelectedValue;
            MiningMode mode = int.TryParse(modeStr, out int modeInt)
                ? (MiningMode)modeInt
                : MiningMode.Vein;

            config.AllowedBlockPrefixes = prefixes;
            config.MaxBlocks = Math.Clamp(maxBlocks, 1, 512);
            config.ExpansionRadius = Math.Clamp(expRadius, 0, 10);
            config.Mode = mode;
            OnSaved?.Invoke(config);
        }

        public override bool DisableMouseGrab => true;
    }
}
