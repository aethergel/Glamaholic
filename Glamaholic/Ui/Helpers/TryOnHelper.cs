using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Glamaholic.Ui.Helpers {
    internal class TryOnHelper {
        private const string PlateName = "Fitting Room";

        private PluginUi Ui { get; }
        private string _nameInput = PlateName;

        internal TryOnHelper(PluginUi ui) {
            this.Ui = ui;
        }

        internal unsafe void Draw() {
            if (!this.Ui.Plugin.Config.ShowTryOnMenu) {
                return;
            }

            var tryOnAddon = Service.GameGui.GetAddonByName("Tryon", 1);
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (tryOnAddon == null || !tryOnAddon.IsVisible) {
                return;
            }

            var right = Service.Interface.InstalledPlugins.Any(state => state.InternalName == "ItemSearchPlugin");
            HelperUtil.DrawHelper(tryOnAddon, "glamaholic-helper-try-on", right, this.DrawDropdown);
        }

        private void DrawDropdown() {
            if (ImGui.Selectable($"Open {Plugin.Name}")) {
                this.Ui.OpenMainInterface();
            }

            if (ImGui.IsWindowAppearing()) {
                this._nameInput = PlateName;
            }

            if (HelperUtil.DrawCreatePlateMenu(this.Ui, GetTryOnItems, ref this._nameInput)) {
                this._nameInput = PlateName;
            }

            if (Interop.Glamourer.IsAvailable()) {
                if (ImGui.Selectable("Try on Glamourer")) {
                    this.TryOnGlamourer(false);
                }

                if (ImGui.Selectable("Try on Glamourer - New Emperor")) {
                    this.TryOnGlamourer(true);
                }
            }
        }

        private void TryOnGlamourer(bool withEmperor) {
            if (Service.ObjectTable.LocalPlayer == null) return;
            var plate = new SavedPlate("Glamourer") {
                Items = GetTryOnItems(),
                FillWithNewEmperor = withEmperor
            };

            Configuration.SanitisePlate(plate);
            Interop.Glamourer.TryOn(Service.ObjectTable.LocalPlayer.ObjectIndex, plate);
        }

        private static unsafe Dictionary<PlateSlot, SavedGlamourItem> GetTryOnItems() {
            // see file footer for information about these offsets

            var agent = AgentTryon.Instance();
            var firstItem = (nint) agent + 0x370;

            var items = new Dictionary<PlateSlot, SavedGlamourItem>();

            for (var i = 0; i < 12; i++) {
                var item = (TryOnItem*) (firstItem + i * 28);
                if (item->Slot == 14 || item->ItemId == 0) {
                    continue;
                }

                int slot = item->Slot > 5 ? item->Slot - 1 : item->Slot;

                var itemId = item->ItemId;
                if (item->GlamourId != 0) {
                    itemId = item->GlamourId;
                }

                var stain1 = item->StainPreview1 == 0
                    ? item->Stain1
                    : item->StainPreview1;

                var stain2 = item->StainPreview2 == 0
                    ? item->Stain2
                    : item->StainPreview2;

                // for some reason, this still accounts for belts in EW

                items[(PlateSlot) slot] = new SavedGlamourItem {
                    ItemId = itemId % Util.HqItemOffset,
                    Stain1 = stain1,
                    Stain2 = stain2,
                };
            }

            return items;
        }

        [StructLayout(LayoutKind.Explicit, Size = 28)]
        private readonly struct TryOnItem {
            [FieldOffset(0)]
            internal readonly byte Slot;

            [FieldOffset(2)]
            internal readonly byte Stain1;

            [FieldOffset(3)]
            internal readonly byte Stain2;

            [FieldOffset(4)]
            internal readonly byte StainPreview1;

            [FieldOffset(5)]
            internal readonly byte StainPreview2;

            [FieldOffset(12)]
            internal readonly uint ItemId;

            [FieldOffset(16)]
            internal readonly uint GlamourId;
        }
    }
}

/*
AgentTryOn offsets:

The script @ IDA/update.py should be used first. If the script is no longer working, then the information below can be used to update the items offset.
  
Locate Client::UI::Agent::AgentTryon.TryOn

Follow the call which passes an agent as the first argument and all original arguments afterward:
    agent = Client::UI::Agent::AgentModule_GetAgentByInternalId(v14, 0x9BLL);
    v16 = agent;
    if ( !agent )
        return 0;
    if ( (*(*agent + 48LL))(agent) && *(v16 + 1752) == 0xE0000000 )
        *(v16 + 856) = this;
    else
        sub_140A9E880(v16, this);
    sub_140A9EDF0(v16, a2, a3, a4, a5, a6); // <--- this function is responsible for updating TryOn items

You should find a block which iterates the item array:
    if ( !*(a1 + 870) )
    {
        v13 = 0;
        v14 = a1 + 880; // <-- 880 is the array base
        while ( !*(v14 + 12) || *v14 >= 2u )
        {
        ++v13;
        v14 += 28LL; // <- 28 is the element size
        if ( v13 >= 14 ) // 14 is the length of the array
            goto LABEL_14;
        }
        if ( *(a1 + 838) )
        {
        *(a1 + 873) = 1;
        *(a1 + 832) = 1;
        *(a1 + 838) = 0;
        }
    LABEL_14:
         sub_140A9F0E0(a1);
    }
*/