using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using static FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMiragePrismMiragePlateData;

namespace Glamaholic {
    internal class GameFunctions : IDisposable {
        #region Dynamic
        private static class FunctionDelegates {
            internal unsafe delegate int GetCabinetItemIdDelegate(Cabinet* _this, uint baseItemId);
        }

        /* 
         * Returns the cabinet id for an item in the Armoire or -1 if not found.
         * 
         * _this should always be UIState::Cabinet
         * 
         * Updating:
         * - Xrefs:
         *   - AgentCabinet_ReceiveEvent: before call to SetSelectedItemData
         *   - ...
         */
        [Signature("E8 ?? ?? ?? ?? 44 8B 0B 44 8B C0")]
        private readonly FunctionDelegates.GetCabinetItemIdDelegate GetCabinetItemId = null!;

        #endregion

        private Plugin Plugin { get; }
        private readonly List<uint> _filterIds = new();
        private static List<PrismBoxCachedItem> _cachedDresserItems = [];
        private static int _dresserItemSlotsUsed = 0;

        internal GameFunctions(Plugin plugin) {
            this.Plugin = plugin;
            Service.GameInteropProvider.InitializeFromAttributes(this);

            Service.ChatGui.ChatMessage += this.OnChat;
            Service.Framework.Update += OnFrameworkUpdate;
        }

        public void Dispose() {
            Service.ChatGui.ChatMessage -= this.OnChat;
            Service.Framework.Update -= OnFrameworkUpdate;
        }

        private void OnChat(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled) {
            if (this._filterIds.Count == 0 || type != XivChatType.SystemMessage) {
                return;
            }

            if (message.Payloads.Any(payload => payload is ItemPayload item && this._filterIds.Remove(item.ItemId))) {
                isHandled = true;
            }
        }

        private unsafe void OnFrameworkUpdate(IFramework framework) {
            var agent = AgentMiragePrismPrismBox.Instance();
            if (agent == null)
                return;

            if (!agent->IsAddonReady() || agent->Data == null)
                return;

            // TODO move back to agent-Data->UsedSlots once CS has updated
            //if (agent->Data->UsedSlots == _dresserItemSlotsUsed)
            //    return;

            int usedSlots = agent->Data->UsedSlots;

            _cachedDresserItems.Clear();
            foreach (var item in agent->Data->PrismBoxItems) {
                if (item.ItemId == 0 || item.Slot >= 800)
                    continue;

                _cachedDresserItems.Add(new PrismBoxCachedItem {
                    Name = item.Name.ToString(),
                    Slot = item.Slot,
                    ItemId = item.ItemId,
                    IconId = item.IconId,
                    Stain1 = item.Stains[0],
                    Stain2 = item.Stains[1],
                });
            }

            _dresserItemSlotsUsed = agent->Data->UsedSlots;
        }

        private static unsafe AgentMiragePrismMiragePlate* MiragePlateAgent => AgentMiragePrismMiragePlate.Instance();

        internal unsafe Cabinet* Armoire => &UIState.Instance()->Cabinet;

        internal unsafe bool ArmoireLoaded => this.Armoire->IsCabinetLoaded();

        internal unsafe string? ExamineName => UIState.Instance()->Inspect.NameString;

        internal static unsafe List<PrismBoxCachedItem> DresserContents {
            get => _cachedDresserItems;
        }

        internal static unsafe Dictionary<PlateSlot, SavedGlamourItem>? CurrentPlate {
            get {
                var agent = MiragePlateAgent;
                if (agent == null) {
                    return null;
                }

                var data = *(AgentMiragePrismMiragePlateData**) ((nint) agent + 0x28);
                if (data == null)
                    return null;

                var plate = new Dictionary<PlateSlot, SavedGlamourItem>();
                foreach (var slot in Enum.GetValues<PlateSlot>()) {
                    ref var item = ref data->Items[(int) slot];

                    if (item.ItemId == 0)
                        continue;

                    var stain1 =
                        item.PreviewStain1 != 0
                            ? item.PreviewStain1
                            : item.Stain1;

                    var stain2 =
                        item.PreviewStain2 != 0
                            ? item.PreviewStain2
                            : item.Stain2;

                    plate[slot] = new SavedGlamourItem {
                        ItemId = item.ItemId,
                        Stain1 = stain1,
                        Stain2 = stain2,
                    };
                }

                return plate;
            }
        }

        internal unsafe bool IsInArmoire(uint itemId) {
            var row = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Cabinet>()!.FirstOrNull(row => row.Item.RowId == itemId);
            if (row == null) {
                return false;
            }

            return this.Armoire->IsItemInCabinet((int) row.Value.RowId);
        }

        internal unsafe void LoadPlate(SavedPlate plate) {
            Plugin.LogTroubleshooting("Begin LoadPlate()");

            var agent = MiragePlateAgent;
            if (agent == null) {
                return;
            }

            var data = *(AgentMiragePrismMiragePlateData**) ((nint) agent + 0x28);
            Plugin.LogTroubleshooting($"AgentMiragePrismMiragePlateData*: {(nint) data:X16}");

            if (data == null)
                return;

            var dresser = DresserContents;
            var current = CurrentPlate;
            var usedStains = new Dictionary<(uint, uint), uint>();

            var initialSlot = data->SelectedItemIndex;
            foreach (var slot in Enum.GetValues<PlateSlot>()) {
                if (!plate.Items.TryGetValue(slot, out var item)) {
                    if (!plate.FillWithNewEmperor)
                        continue;

                    uint emperorId = Util.GetEmperorItemForSlot(slot);
                    if (emperorId != 0)
                        item = new SavedGlamourItem { ItemId = emperorId };
                    else
                        continue;
                }

                if (current != null && current.TryGetValue(slot, out var currentItem)) {
                    if (currentItem.ItemId == item.ItemId
                        && currentItem.Stain1 == item.Stain1
                        && currentItem.Stain2 == item.Stain2) {
                        // ignore already-correct items
                        Plugin.LogTroubleshooting($"Skipping {slot}: expected item state is already in plate");
                        continue;
                    }
                }

                data->SelectedItemIndex = (uint) slot;
                if (item.ItemId == 0) {
                    Plugin.LogTroubleshooting($"Clearing {slot}: slot is explicitly empty");

                    uint previousContextSlot = data->ContextMenuItemIndex;
                    data->ContextMenuItemIndex = (uint) slot;

                    AtkValue rv;
                    agent->ReceiveEvent(&rv, null, 0, 1); // "Remove Item Image from Plate"

                    data->ContextMenuItemIndex = previousContextSlot;
                    continue;
                }

                var source = ItemSource.PrismBox;

                var info = (-1, 0u, (byte) 0, (byte) 0);

                Plugin.LogTroubleshooting($"Searching for {slot} {item.ItemId} ({item.Stain1}, {item.Stain2})");

                // find an item in the dresser that matches
                var matchingIds = dresser.FindAll(mirage => (mirage.ItemId % Util.ItemModifierMod) == item.ItemId);
                Plugin.LogTroubleshooting($"Dresser has {matchingIds.Count} items matching {item.ItemId}");



                if (matchingIds.Count == 0) {
                    int cabinetId = GetCabinetItemId(&UIState.Instance()->Cabinet, item.ItemId);
                    if (cabinetId != -1 && this.Armoire->IsItemInCabinet(cabinetId)) {
                        source = ItemSource.Cabinet;

                        info = (cabinetId, item.ItemId, 0, 0);

                        Plugin.LogTroubleshooting($"Item {item.ItemId} found in armoire with cabinet id {cabinetId}");
                    }
                } else {
                    // try to find an item with matching stains
                    var idx = matchingIds.FindIndex(mirage =>
                        mirage.Stain1 == item.Stain1
                        && mirage.Stain2 == item.Stain2);

                    bool matchWithStains = idx != -1;

                    if (idx == -1)
                        idx = 0;

                    var mirage = matchingIds[idx];
                    info = ((int) mirage.Slot, mirage.ItemId, mirage.Stain1, mirage.Stain2);

                    Plugin.LogTroubleshooting($"Item {item.ItemId} found in dresser at slot {mirage.Slot} with stains {mirage.Stain1}, {mirage.Stain2} ({(matchWithStains ? "matched" : "mismatched")})");
                }

                if (info.Item1 == -1) {
                    Plugin.LogTroubleshooting($"Item {item.ItemId} could not be found, skipping!");
                    continue;
                }

                Plugin.LogTroubleshooting($"SetGlamourPlateItemSlot({source}, {info.Item1}, {info.Item2}, {info.Item3}, {info.Item4})");
                AgentMiragePrismMiragePlate.Instance()->SetSelectedItemData(
                    source,
                    (uint) info.Item1, // slot or cabinet id
                    info.Item2, // item id
                    info.Item3, // stain 1
                    info.Item4  // stain 2
                );

                // TODO
                if (item.Stain1 != info.Item3 || item.Stain2 != info.Item4) {
                    // mirage in dresser did not have stain for this item, so apply it
                    Plugin.LogTroubleshooting($"Applying stains to {slot}: {item.Stain1}, {item.Stain2}");

                    uint previousContextSlot = data->ContextMenuItemIndex;
                    data->ContextMenuItemIndex = (uint) slot;

                    // item loading for plates is deferred as of patch 7.1
                    // so we must set the flags ourselves in order to activate the second dye slot immediately
                    if (item.Stain2 != 0)
                        data->Items[(int) slot].Flags = 0x20;

                    this.ApplyStains(slot, item, usedStains);

                    data->ContextMenuItemIndex = previousContextSlot;
                }
            }

            // restore initial slot, since changing this does not update the ui
            data->SelectedItemIndex = initialSlot;
            data->HasChanges = true;

            Plugin.LogTroubleshooting("End LoadPlate()");
        }

        private static readonly InventoryType[] PlayerInventories = {
            InventoryType.Inventory1,
            InventoryType.Inventory2,
            InventoryType.Inventory3,
            InventoryType.Inventory4,
        };

        private unsafe InventoryItem* SelectStainItem(byte stainId, Dictionary<(uint, uint), uint> usedStains, out uint bestItemId) {
            var inventory = InventoryManager.Instance();
            var transient = Service.DataManager.GetExcelSheet<Lumina.Excel.Sheets.StainTransient>()!.GetRowOrDefault(stainId);

            InventoryItem* item = null;

            bestItemId = transient?.Item1.ValueNullable?.RowId ?? (transient?.Item2.ValueNullable?.RowId ?? 0);

            var items = new[] { transient?.Item1.ValueNullable, transient?.Item2.ValueNullable };
            foreach (var dyeItem in items) {
                if (dyeItem == null || dyeItem.Value.RowId == 0) {
                    continue;
                }

                foreach (var type in PlayerInventories) {
                    var inv = inventory->GetInventoryContainer(type);
                    if (inv == null) {
                        continue;
                    }

                    for (var i = 0; i < inv->Size; i++) {
                        var address = ((uint) type, (uint) i);
                        var invItem = inv->Items[i];

                        if (invItem.ItemId != dyeItem.Value.RowId) {
                            continue;
                        }

                        if (usedStains.TryGetValue(address, out var numUsed) && numUsed >= invItem.Quantity) {
                            continue;
                        }

                        // first one that we find in the inventory is the one we'll use
                        item = &inv->Items[i];
                        bestItemId = invItem.ItemId;

                        if (usedStains.ContainsKey(address)) {
                            usedStains[address] += 1;
                        } else {
                            usedStains[address] = 1;
                        }

                        goto NoBreakLabels;
                    }
                }

            NoBreakLabels:
                {
                }
            }

            return item;
        }

        private unsafe void ApplyStains(PlateSlot slot, SavedGlamourItem item, Dictionary<(uint, uint), uint> usedStains) {
            var stain1Item = SelectStainItem(item.Stain1, usedStains, out var stain1ItemId);
            var stain2Item = SelectStainItem(item.Stain2, usedStains, out var stain2ItemId);

            Plugin.LogTroubleshooting($"SetGlamourPlateSlotStains({(stain1Item != null ? stain1Item->Slot : 0)}, {item.Stain1}, {stain1ItemId}, {(stain2Item != null ? stain2Item->Slot : 0)}, {item.Stain2}, {stain2ItemId})");
            AgentMiragePrismMiragePlate.Instance()->SetSelectedItemStains(stain1Item, item.Stain1, stain1ItemId,
                                                                          stain2Item, item.Stain2, stain2ItemId);
        }

        internal void TryOn(uint itemId, byte stainId, byte stainId2, bool suppress = true) {
            if (suppress) {
                this._filterIds.Add(itemId);
            }

            AgentTryon.TryOn(0, itemId % Util.HqItemOffset, stainId, stainId2);
        }

        internal struct PrismBoxCachedItem {
            public string Name { get; set; }
            public uint Slot { get; set; }
            public uint ItemId { get; set; }
            public uint IconId { get; set; }
            public byte Stain1 { get; set; }
            public byte Stain2 { get; set; }
        }
    }
}
