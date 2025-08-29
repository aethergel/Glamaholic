using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Glamaholic.Ui;
using Glamaholic.Ui.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Glamaholic {
    internal class PluginUi : IDisposable {
        internal Plugin Plugin { get; }

        private MainInterface MainInterface { get; }
        private EditorHelper EditorHelper { get; }
        private ExamineHelper ExamineHelper { get; }
        private TryOnHelper TryOnHelper { get; }
        internal List<AlternativeFinder> AlternativeFinders { get; } = new();
        internal List<(string, string)> Help { get; } = new();

        internal PluginUi(Plugin plugin) {
            this.Plugin = plugin;

            foreach (var entry in Resourcer.Resource.AsString("help.txt").Split("---")) {
                var lines = entry.Trim().Split(new[] { "\n", "\r\n" }, StringSplitOptions.TrimEntries);
                if (lines.Length == 0 || !lines[0].StartsWith("#")) {
                    continue;
                }

                var title = lines[0][1..].Trim();
                var content = string.Join(" ", lines[1..]
                    .SkipWhile(string.IsNullOrWhiteSpace)
                    .Select(line => string.IsNullOrWhiteSpace(line) ? "\n" : line));
                this.Help.Add((title, content));
            }

            this.MainInterface = new MainInterface(this);
            this.EditorHelper = new EditorHelper(this);
            this.ExamineHelper = new ExamineHelper(this);
            this.TryOnHelper = new TryOnHelper(this);

            Service.Interface.UiBuilder.Draw += this.Draw;
            Service.Interface.UiBuilder.OpenConfigUi += this.OpenMainInterface;
            Service.Interface.UiBuilder.OpenMainUi += this.OpenMainInterface;
        }

        public void Dispose() {
            Service.Interface.UiBuilder.OpenMainUi -= this.OpenMainInterface;
            Service.Interface.UiBuilder.OpenConfigUi -= this.OpenMainInterface;
            Service.Interface.UiBuilder.Draw -= this.Draw;
        }

        internal void OpenMainInterface() {
            this.MainInterface.Open();
        }

        internal void ToggleMainInterface() {
            this.MainInterface.Toggle();
        }

        internal IDalamudTextureWrap? GetIcon(ushort id) {
            var icon = Service.TextureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(id)).GetWrapOrDefault();
            return icon;
        }

        private void Draw() {
            this.MainInterface.Draw();
            this.EditorHelper.Draw();
            this.ExamineHelper.Draw();
            this.TryOnHelper.Draw();

            this.AlternativeFinders.RemoveAll(finder => {
                finder.Draw();
                return !finder.Visible;
            });
        }

        internal void SwitchPlate(Guid plateId, bool scrollTo = false) =>
            this.MainInterface.SwitchPlate(plateId, scrollTo);

        internal unsafe void TryOn(IEnumerable<SavedGlamourItem> items) {
            void SetTryOnSave(bool save) {
                var tryOnAgent = AgentTryon.Instance();
                if (tryOnAgent != null)
                    *(byte*) ((nint) tryOnAgent + 0x366) = (byte) (save ? 1 : 0);
            }

            SetTryOnSave(false);
            foreach (var mirage in items) {
                if (mirage.ItemId == 0) {
                    continue;
                }

                this.Plugin.Functions.TryOn(mirage.ItemId, mirage.Stain1, mirage.Stain2);
                SetTryOnSave(true);
            }
        }

        internal unsafe void TryOnPlate(SavedPlate plate) {
            void SetTryOnSave(bool save) {
                var tryOnAgent = AgentTryon.Instance();
                if (tryOnAgent != null)
                    *(byte*) ((nint) tryOnAgent + 0x366) = (byte) (save ? 1 : 0);
            }

            SetTryOnSave(false);
            foreach (var slot in Enum.GetValues<PlateSlot>()) {
                if (!plate.Items.TryGetValue(slot, out var item) || item.ItemId == 0) {
                    if (plate.FillWithNewEmperor) {
                        uint emperor = Util.GetEmperorItemForSlot(slot);
                        if (emperor != 0) {
                            this.Plugin.Functions.TryOn(emperor, 0, 0);
                            SetTryOnSave(true);
                        }
                    }

                    continue;
                }

                this.Plugin.Functions.TryOn(item.ItemId, item.Stain1, item.Stain2);
                SetTryOnSave(true);
            }
        }
    }
}

/*
AgentTryOn "Save/Delete Outfit"

The script @ IDA/update.py should be used first. If the script is no longer working, then the information below can be used to update the toggle offset.

Client::UI::Agent::AgentTryon_ReceiveEvent
--
case 0x11u:
   v38 = 0;
   if ( *(a1 + 862) ) // <-- cmp [rbx+35Eh], bpl (862 is offset)
   {
     v49 = *(a1 + 16);
     v50 = (*(*v49 + 48LL))(v49, *v49, &_ImageBase);
     AddonText = Client::UI::Misc::RaptureTextModule_GetAddonText(v50, 0x96Eu);
     v52 = (*(*v49 + 64LL))(v49);
     *(a1 + 852) = Client::UI::RaptureAtkModule_OpenYesNo(
                     v52,
                     AddonText,
                     qword_142623A18,
                     qword_142623A20,
                     a1,
                     2LL,
                     0,
                     *(a1 + 32),
                     4,
                     0LL,
                     0LL,
                     0,
                     0LL,
                     0,
                     1u,
                     0,
                     0,
                     0,
                     -1,
                     0,
                     0,
                     0);
   }
   else
   {
     *(a1 + 862) = 1; // <-- (862 is offset)
   }
 */