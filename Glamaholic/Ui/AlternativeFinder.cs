using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Glamaholic.Ui {
    internal class AlternativeFinder {
        private Guid Id { get; } = Guid.NewGuid();
        internal bool Visible = true;

        private PluginUi Ui { get; }
        private Item Item { get; }
        private List<Item> Alternatives { get; } = new();

        internal AlternativeFinder(PluginUi ui, Item item) {
            this.Ui = ui;
            this.Item = item;

            var info = ModelInfo(item.ModelMain);

            foreach (var row in Service.DataManager.GetExcelSheet<Item>()!) {
                if (Util.IsItemSkipped(row) || row.EquipSlotCategory.RowId != this.Item.EquipSlotCategory.RowId || info != ModelInfo(row.ModelMain)) {
                    continue;
                }

                this.Alternatives.Add(row);
            }
        }

        internal void Draw() {
            ImGui.SetNextWindowSize(new Vector2(250, 350), ImGuiCond.Appearing);
            ImGui.SetNextWindowPos(ImGui.GetMousePos(), ImGuiCond.Appearing);

            const ImGuiWindowFlags flags = ImGuiWindowFlags.NoSavedSettings;
            if (!ImGui.Begin($"Alternative Finder: {this.Item.Name}##{this.Id}", ref this.Visible, flags)) {
                ImGui.End();
                return;
            }

            this.DrawInner();

            ImGui.End();
        }

        private void DrawInner() {
            ImGui.Columns(2);
            ImGui.SetColumnWidth(0, MainInterface.IconSize + ImGui.GetStyle().ItemSpacing.X * 2);
            var icon = this.Ui.GetIcon(this.Item.Icon);
            if (icon != null) {
                ImGui.Image(icon.Handle, new Vector2(48));
            }

            ImGui.NextColumn();

            ImGui.TextUnformatted("Click: link to chat");
            ImGui.TextUnformatted("Right-click: try on");

            ImGui.Columns();

            ImGui.Separator();

            this.DrawAlternatives();
        }

        private void DrawAlternatives() {
            if (!ImGui.BeginChild($"{this.Id} alternatives")) {
                return;
            }

            foreach (var alt in this.Alternatives) {
                if (ImGui.Selectable($"##{alt.RowId}", alt.RowId == this.Item.RowId)) {
                    this.LinkItem(alt);
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
                    this.Ui.Plugin.Functions.TryOn(alt.RowId, 0, 0, false);
                }

                ImGui.SameLine();

                if (alt.DyeCount == 0) {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int) ImGuiCol.TextDisabled]);
                }

                Util.TextIcon(FontAwesomeIcon.FillDrip);

                if (alt.DyeCount == 0) {
                    ImGui.PopStyleColor();
                }

                ImGui.SameLine();
                ImGui.TextUnformatted(alt.Name.ExtractText());
            }

            ImGui.EndChild();
        }

        private static (ushort, ushort, ushort, ushort) ModelInfo(ulong raw) {
            // return (ushort) (raw & 0xFFFF);
            var primaryKey = (ushort) (raw & 0xFFFF);
            var secondaryKey = (ushort) ((raw >> 16) & 0xFFFF);
            var variant = (ushort) ((raw >> 32) & 0xFFFF);
            var dye = (ushort) ((raw >> 48) & 0xFFFF);

            if (variant != 0) {
                // weapon
                return (primaryKey, secondaryKey, variant, dye);
            }

            return (primaryKey, 0, 0, 0);
        }

        private void LinkItem(Item item) {
            var payloadList = new List<Payload> {
                new UIForegroundPayload((ushort) (0x223 + item.Rarity * 2)),
                new UIGlowPayload((ushort) (0x224 + item.Rarity * 2)),
                new ItemPayload(item.RowId, false),
                new UIForegroundPayload(500),
                new UIGlowPayload(501),
                new TextPayload($"{(char) SeIconChar.LinkMarker}"),
                new UIForegroundPayload(0),
                new UIGlowPayload(0),
            };
            payloadList.AddRange(SeString.Parse(item.Name).Payloads); // TODO review
            payloadList.AddRange(new[] {
                new RawPayload(new byte[] { 0x02, 0x27, 0x07, 0xCF, 0x01, 0x01, 0x01, 0xFF, 0x01, 0x03 }),
                new RawPayload(new byte[] { 0x02, 0x13, 0x02, 0xEC, 0x03 }),
            });

            var payload = new SeString(payloadList);

            Service.ChatGui.Print(new XivChatEntry {
                Message = payload,
            });
        }
    }
}
