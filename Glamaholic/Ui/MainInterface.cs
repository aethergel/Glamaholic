﻿using Dalamud.Game;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Glamaholic.Interop;
using ImGuiNET;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Glamaholic.Ui {
    internal class MainInterface {
        internal const int IconSize = 48;

        private static readonly PlateSlot[] LeftSide = {
            PlateSlot.MainHand,
            PlateSlot.Head,
            PlateSlot.Body,
            PlateSlot.Hands,
            PlateSlot.Legs,
            PlateSlot.Feet,
        };

        private static readonly PlateSlot[] RightSide = {
            PlateSlot.OffHand,
            PlateSlot.Ears,
            PlateSlot.Neck,
            PlateSlot.Wrists,
            PlateSlot.RightRing,
            PlateSlot.LeftRing,
        };

        private PluginUi Ui { get; }
        private ImmutableList<Item> FilteredItems { get; set; }

        private FilterInfo? PlateFilter { get; set; }

        private bool _visible;
        private int _dragging = -1;
        private Guid? _selectedPlateId = null;
        private bool _scrollToSelected;
        private string _plateFilter = string.Empty;
        private bool _showRename;
        private string _renameInput = string.Empty;
        private bool _deleteConfirm;
        private bool _editing;
        private SavedPlate? _editingPlate;
        private Guid? _editingPlateId = null;
        private string _itemFilter = string.Empty;
        private string _dyeFilter = string.Empty;
        private volatile bool _ecImporting;
        private readonly Dictionary<string, Stopwatch> _timedMessages = new();
        private string _tagInput = string.Empty;
        private int _editingItemDyeCount = 0;
        private DateTime _dyesCopiedTime = DateTime.MinValue;
        private bool _massImport = false;
        private string _massImportLastURL = string.Empty;
        private string _massImportMessage = string.Empty;
        private int _massImportTarget = 0;
        private string _newFolderName = string.Empty;

        private Guid? _contextMenuNodeId = null;
        private bool _confirmDeleteNode = false;
        private bool _confirmDeleteFolder = false;
        private int? _pendingFolderAction = null; // 0 = delete all, 1 = move out

        internal MainInterface(PluginUi ui) {
            this.Ui = ui;
        }

        internal void Open() {
            this._visible = true;
        }

        internal void Toggle() {
            this._visible ^= true;
        }

        internal void Draw() {
            this.HandleTimers();

            if (this._massImport)
                this.DrawMassImportWindow();

            if (!this._visible) {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(415, 650), ImGuiCond.FirstUseEver);

            if (!ImGui.Begin(Plugin.Name, ref this._visible, ImGuiWindowFlags.MenuBar)) {
                ImGui.End();
                return;
            }

            this.DrawInner();

            ImGui.End();
        }

        private static bool IsValidEorzeaCollectionUrl(string urlString) {
            if (!Uri.TryCreate(urlString, UriKind.Absolute, out var url)) {
                return false;
            }

            return url.Host == "ffxiv.eorzeacollection.com" && url.AbsolutePath.StartsWith("/glamour/");
        }

        private void DrawMenuBar() {
            if (!ImGui.BeginMenuBar()) {
                return;
            }

            if (ImGui.BeginMenu("Plates")) {
                if (ImGui.MenuItem("New")) {
                    var id = this.Ui.Plugin.Config.AddPlate(new SavedPlate("Untitled Plate"));
                    TreeUtils.Sort(this.Ui.Plugin.Config.Plates);
                    this.Ui.Plugin.SaveConfig();
                    this.SwitchPlate(id, true);
                }

                if (ImGui.MenuItem("Import from Clipboard")) {
                    var json = Util.GetClipboardText();
                    try {
                        var plate = JsonConvert.DeserializeObject<SharedPlate>(json);
                        if (plate != null) {
                            var id = this.Ui.Plugin.Config.AddPlate(plate.ToPlate());
                            TreeUtils.Sort(this.Ui.Plugin.Config.Plates);
                            this.Ui.Plugin.SaveConfig();
                            this.Ui.SwitchPlate(id);
                        }
                    } catch (Exception ex) {
                        Service.Log.Warning(ex, "Failed to import glamour plate");
                    }
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Eorzea Collection")) {
                var validUrl = IsValidEorzeaCollectionUrl(Util.GetClipboardText());
                if (ImGui.BeginMenu("Import from URL", validUrl && !this._ecImporting)) {
                    if (ImGui.MenuItem("New Plate"))
                        this.ImportEorzeaCollection(Util.GetClipboardText(), ECImportTarget.NewPlate);

                    if (ImGui.MenuItem("Try On"))
                        this.ImportEorzeaCollection(Util.GetClipboardText(), ECImportTarget.TryOn);

                    if (Interop.Glamourer.IsAvailable() && ImGui.MenuItem("Try On (Glamourer)"))
                        this.ImportEorzeaCollection(Util.GetClipboardText(), ECImportTarget.TryOnGlamourer);

                    ImGui.EndMenu();
                }

                if (ImGui.MenuItem("Mass Import")) {
                    _massImport = true;
                    _massImportLastURL = Util.GetClipboardText();
                    _massImportMessage = "Waiting for URL to be copied...";
                }

                ImGui.EndMenu();
            }

            var anyChanged = false;
            if (ImGui.BeginMenu("Settings")) {
                anyChanged |= ImGui.MenuItem("Show plate editor menu", null, ref this.Ui.Plugin.Config.ShowEditorMenu);
                anyChanged |= ImGui.MenuItem("Show examine window menu", null, ref this.Ui.Plugin.Config.ShowExamineMenu);
                anyChanged |= ImGui.MenuItem("Show try on menu", null, ref this.Ui.Plugin.Config.ShowTryOnMenu);
                ImGui.Separator();
                anyChanged |= ImGui.MenuItem("Show Ko-fi button", null, ref this.Ui.Plugin.Config.ShowKofiButton);
                ImGui.Separator();
                anyChanged |= ImGui.MenuItem("Troubleshooting mode", null, ref this.Ui.Plugin.Config.TroubleshootingMode);

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help")) {
                foreach (var (title, content) in this.Ui.Help) {
                    if (!ImGui.BeginMenu(title)) {
                        continue;
                    }

                    ImGui.PushTextWrapPos(ImGui.CalcTextSize("0").X * 60f * ImGuiHelpers.GlobalScale);
                    ImGui.TextUnformatted(content);
                    ImGui.PopTextWrapPos();

                    ImGui.EndMenu();
                }

                ImGui.EndMenu();
            }

            if (this.Ui.Plugin.Config.ShowKofiButton) {
                const string kofiText = "Support Anna on Ko-fi";
                var kofiTextSize = ImGui.CalcTextSize(kofiText);
                ImGui.GetWindowDrawList().AddRectFilled(
                    ImGui.GetCursorScreenPos(),
                    ImGui.GetCursorScreenPos() + kofiTextSize + ImGui.GetStyle().ItemInnerSpacing * 2,
                    0xFF5B5EFF
                );
                ImGui.PushStyleColor(ImGuiCol.Text, 0xFFFFFFFF);
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0x00000000);
                if (ImGui.MenuItem(kofiText)) {
                    Process.Start(new ProcessStartInfo("https://ko-fi.com/lojewalo") {
                        UseShellExecute = true,
                    });
                }

                ImGui.PopStyleColor(2);
            }

            if (anyChanged) {
                this.Ui.Plugin.SaveConfig();
            }

            ImGui.EndMenuBar();
        }

        private enum ECImportTarget {
            NewPlate,
            TryOn,
            TryOnGlamourer,
        }

        private void ImportEorzeaCollection(string url, ECImportTarget target) {
            if (!IsValidEorzeaCollectionUrl(url) || Service.ClientState.LocalPlayer == null) {
                return;
            }

            this._ecImporting = true;

            int playerIndex = Service.ClientState.LocalPlayer.ObjectIndex;

            Task.Run(async () => {
                var import = await EorzeaCollection.ImportFromURL(url);

                this._ecImporting = false;

                if (import == null) {
                    if (this._massImport)
                        this._massImportMessage = $"Import failed.. copy a new link to try again";

                    return;
                }

                if (this._massImport)
                    this._massImportMessage = $"Imported {import.Name}";

                switch (target) {
                    case ECImportTarget.NewPlate:
                        import.Tags.Add("Eorzea Collection");
                        var id = this.Ui.Plugin.Config.AddPlate(import);
                        TreeUtils.Sort(this.Ui.Plugin.Config.Plates);
                        this.Ui.Plugin.SaveConfig();
                        this.SwitchPlate(id, true);
                        break;
                    case ECImportTarget.TryOn:
                        this.Ui.TryOnPlate(import);
                        break;
                    case ECImportTarget.TryOnGlamourer:
                        Interop.Glamourer.TryOn(playerIndex, import);
                        break;
                }
            });
        }

        private void DrawPlateList() {
            // Begin outer child for the whole left panel
            if (!ImGui.BeginChild("plate list", new Vector2(205 * ImGuiHelpers.GlobalScale, 0), true)) {
                return;
            }

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##plate-filter", "Search...", ref this._plateFilter, 512, ImGuiInputTextFlags.AutoSelectAll)) {
                this.PlateFilter = this._plateFilter.Length == 0
                    ? null
                    : new FilterInfo(Service.DataManager, this._plateFilter);
            }

            float buttonBarHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().FramePadding.Y * 2;
            float scrollAreaHeight = ImGui.GetContentRegionAvail().Y - buttonBarHeight;
            if (scrollAreaHeight < 50f) scrollAreaHeight = 50f;

            Guid? pendingMoveNodeId = null;
            Guid? pendingMoveTargetId = null; // null means move to root

            List<(Guid nodeId, int? folderAction)> deferredActions = new();

            if (ImGui.BeginChild("plate tree", new Vector2(0, scrollAreaHeight), false, ImGuiWindowFlags.HorizontalScrollbar)) {
                void DrawPlateNodes(List<TreeNode> nodes, int depth = 0, string parentPath = "") {
                    foreach (var node in nodes) {
                        if (node is PlateNode leaf) {
                            if (this.PlateFilter != null && !this.PlateFilter.Matches(leaf.Plate)) {
                                continue;
                            }

                            Guid? switchTo = null;
                            bool isSelected = this._selectedPlateId == node.Id;
                            if (ImGui.Selectable($"{new string(' ', depth * 2)}{leaf.Name}##{node.Id}", isSelected)) {
                                switchTo = node.Id;
                            }

                            if (ImGui.BeginDragDropSource()) {
                                unsafe {
                                    var dragId = node.Id;
                                    ImGui.SetDragDropPayload("TREE_NODE", new IntPtr(&dragId), (uint)sizeof(Guid));
                                }

                                ImGui.Text($"Moving '{leaf.Name}'");
                                ImGui.EndDragDropSource();
                            }

                            // Allow drag-drop target for non-folder nodes (move to root)
                            if (ImGui.BeginDragDropTarget()) {
                                unsafe {
                                    var payload = ImGui.AcceptDragDropPayload("TREE_NODE");
                                    if (payload.NativePtr != null && payload.Data != IntPtr.Zero && payload.DataSize == sizeof(Guid)) {
                                        Guid draggedNodeId = *(Guid*) payload.Data;
                                        if (draggedNodeId != node.Id) {
                                            pendingMoveNodeId = draggedNodeId;
                                            pendingMoveTargetId = null; // move to root
                                        }
                                    }
                                }
                                ImGui.EndDragDropTarget();
                            }

                            if (this._scrollToSelected && isSelected) {
                                this._scrollToSelected = false;
                                ImGui.SetScrollHereY(1f);
                            }

                            // Right-click context menu for plate node
                            if (ImGui.BeginPopupContextItem($"plate-node-context-{node.Id}")) {
                                if (ImGui.MenuItem("Delete")) {
                                    _contextMenuNodeId = node.Id;
                                    _confirmDeleteNode = true;
                                }
                                ImGui.EndPopup();
                            }

                            if (_confirmDeleteNode && _contextMenuNodeId == node.Id) {
                                ImGui.OpenPopup($"confirm-delete-plate-{node.Id}");
                            }

                            if (ImGui.BeginPopup($"confirm-delete-plate-{node.Id}")) {
                                ImGui.TextUnformatted("Delete this plate? This cannot be undone.");
                                if (ImGui.Button("Delete")) {
                                    deferredActions.Add((node.Id, null));
                                    _confirmDeleteNode = false;
                                    _contextMenuNodeId = null;
                                    ImGui.CloseCurrentPopup();
                                }

                                ImGui.SameLine();

                                if (ImGui.Button("Cancel")) {
                                    _confirmDeleteNode = false;
                                    _contextMenuNodeId = null;
                                    ImGui.CloseCurrentPopup();
                                }
                                ImGui.EndPopup();
                            }

                            if (switchTo != null) {
                                this.SwitchPlate(switchTo.Value);
                            }
                        } else if (node is FolderNode folder) {
                            string folderPath = parentPath + "/" + folder.Name;
                            ImGui.PushID(folderPath);

                            if (ImGui.TreeNodeEx($"{folder.Name}")) {
                                // Add drag source for expanded folders
                                if (ImGui.BeginDragDropSource()) {
                                    unsafe {
                                        var dragId = node.Id;
                                        ImGui.SetDragDropPayload("TREE_NODE", new IntPtr(&dragId), (uint)sizeof(Guid));
                                    }

                                    ImGui.Text($"Moving folder '{folder.Name}'");
                                    ImGui.EndDragDropSource();
                                }

                                if (ImGui.BeginDragDropTarget()) {
                                    unsafe {
                                        var payload = ImGui.AcceptDragDropPayload("TREE_NODE");
                                        if (payload.NativePtr != null && payload.Data != IntPtr.Zero && payload.DataSize == sizeof(Guid)) {
                                            Guid draggedNodeId = *(Guid*) payload.Data;
                                            if (draggedNodeId != node.Id) {
                                                // Prevent dropping a folder into itself or its descendants
                                                var draggedNode = TreeUtils.FindNodeById(this.Ui.Plugin.Config.Plates, draggedNodeId);
                                                bool isValidDrop = true;
                                                
                                                if (draggedNode is FolderNode draggedFolder) {
                                                    isValidDrop = !IsDescendantOf(this.Ui.Plugin.Config.Plates, node.Id, draggedNodeId);
                                                }
                                                
                                                if (isValidDrop) {
                                                    pendingMoveNodeId = draggedNodeId;
                                                    pendingMoveTargetId = node.Id;
                                                }
                                            }
                                        }
                                    }
                                    ImGui.EndDragDropTarget();
                                }

                                // Right-click context menu for folder node
                                if (ImGui.BeginPopupContextItem($"folder-node-context-{node.Id}")) {
                                    if (ImGui.MenuItem("Delete Folder (and all contents)")) {
                                        _contextMenuNodeId = node.Id;
                                        _pendingFolderAction = 0;
                                        _confirmDeleteFolder = true;
                                    }

                                    if (ImGui.MenuItem("Delete Folder (move contents out)")) {
                                        _contextMenuNodeId = node.Id;
                                        _pendingFolderAction = 1;
                                        _confirmDeleteFolder = true;
                                    }

                                    ImGui.EndPopup();
                                }

                                if (_confirmDeleteFolder && _contextMenuNodeId == node.Id) {
                                    ImGui.OpenPopup($"confirm-delete-folder-{node.Id}");
                                }

                                if (ImGui.BeginPopup($"confirm-delete-folder-{node.Id}")) {
                                    if (_pendingFolderAction == 0) {
                                        ImGui.TextUnformatted("Delete this folder and ALL its contents? This cannot be undone.");
                                    } else {
                                        ImGui.TextUnformatted("Delete this folder and move all contents out?");
                                    }

                                    if (ImGui.Button("Delete")) {
                                        deferredActions.Add((node.Id, _pendingFolderAction));
                                        _confirmDeleteFolder = false;
                                        _contextMenuNodeId = null;
                                        _pendingFolderAction = null;
                                        ImGui.CloseCurrentPopup();
                                    }

                                    ImGui.SameLine();

                                    if (ImGui.Button("Cancel")) {
                                        _confirmDeleteFolder = false;
                                        _contextMenuNodeId = null;
                                        _pendingFolderAction = null;
                                        ImGui.CloseCurrentPopup();
                                    }

                                    ImGui.EndPopup();
                                }

                                if (folder.Children.Count > 0) {
                                    DrawPlateNodes(folder.Children, depth + 1, folderPath);
                                }

                                ImGui.TreePop();
                            } else {
                                // Add drag source for collapsed folders
                                if (ImGui.BeginDragDropSource()) {
                                    unsafe {
                                        var dragId = node.Id;
                                        ImGui.SetDragDropPayload("TREE_NODE", new IntPtr(&dragId), (uint)sizeof(Guid));
                                    }

                                    ImGui.Text($"Moving folder '{folder.Name}'");
                                    ImGui.EndDragDropSource();
                                }

                                // Still allow drag-drop on collapsed folders
                                if (ImGui.BeginDragDropTarget()) {
                                    unsafe {
                                        var payload = ImGui.AcceptDragDropPayload("TREE_NODE");
                                        if (payload.NativePtr != null && payload.Data != IntPtr.Zero && payload.DataSize == sizeof(Guid)) {
                                            Guid draggedNodeId = *(Guid*) payload.Data;
                                            if (draggedNodeId != node.Id) {
                                                // Prevent dropping a folder into itself or its descendants
                                                var draggedNode = TreeUtils.FindNodeById(this.Ui.Plugin.Config.Plates, draggedNodeId);
                                                bool isValidDrop = true;
                                                
                                                if (draggedNode is FolderNode draggedFolder) {
                                                    isValidDrop = !IsDescendantOf(this.Ui.Plugin.Config.Plates, node.Id, draggedNodeId);
                                                }
                                                
                                                if (isValidDrop) {
                                                    pendingMoveNodeId = draggedNodeId;
                                                    pendingMoveTargetId = node.Id;
                                                }
                                            }
                                        }
                                    }
                                    ImGui.EndDragDropTarget();
                                }

                                // Right-click context menu for collapsed folder
                                if (ImGui.BeginPopupContextItem($"folder-node-context-{node.Id}")) {
                                    if (ImGui.MenuItem("Delete Folder (and all contents)")) {
                                        _contextMenuNodeId = node.Id;
                                        _pendingFolderAction = 0;
                                        _confirmDeleteFolder = true;
                                    }
                                    if (ImGui.MenuItem("Delete Folder (move contents out)")) {
                                        _contextMenuNodeId = node.Id;
                                        _pendingFolderAction = 1;
                                        _confirmDeleteFolder = true;
                                    }

                                    ImGui.EndPopup();
                                }

                                if (_confirmDeleteFolder && _contextMenuNodeId == node.Id) {
                                    ImGui.OpenPopup($"confirm-delete-folder-{node.Id}");
                                }

                                if (ImGui.BeginPopup($"confirm-delete-folder-{node.Id}")) {
                                    if (_pendingFolderAction == 0) {
                                        ImGui.TextUnformatted("Delete this folder and ALL its contents? This cannot be undone.");
                                    } else {
                                        ImGui.TextUnformatted("Delete this folder and move all contents out?");
                                    }

                                    if (ImGui.Button("Delete")) {
                                        deferredActions.Add((node.Id, _pendingFolderAction));
                                        _confirmDeleteFolder = false;
                                        _contextMenuNodeId = null;
                                        _pendingFolderAction = null;
                                        ImGui.CloseCurrentPopup();
                                    }

                                    ImGui.SameLine();

                                    if (ImGui.Button("Cancel")) {
                                        _confirmDeleteFolder = false;
                                        _contextMenuNodeId = null;
                                        _pendingFolderAction = null;
                                        ImGui.CloseCurrentPopup();
                                    }

                                    ImGui.EndPopup();
                                }
                            }

                            ImGui.PopID();
                        }
                    }
                }

                DrawPlateNodes(this.Ui.Plugin.Config.Plates);

                // Add drag-drop target for root (below the tree)
                if (ImGui.BeginDragDropTarget()) {
                    unsafe {
                        var payload = ImGui.AcceptDragDropPayload("TREE_NODE");
                        if (payload.NativePtr != null && payload.Data != IntPtr.Zero && payload.DataSize == sizeof(Guid)) {
                            Guid draggedNodeId = *(Guid*) payload.Data;
                            pendingMoveNodeId = draggedNodeId;
                            pendingMoveTargetId = null; // move to root
                        }
                    }
                    ImGui.EndDragDropTarget();
                }

                if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && this._dragging != -1) {
                    this._dragging = -1;
                    this.Ui.Plugin.SaveConfig();
                }

                ImGui.EndChild();
            }

            // Process deferred actions using new TreeUtils
            if (pendingMoveNodeId.HasValue) {
                TreeUtils.MoveNodeToFolder(this.Ui.Plugin.Config.Plates, pendingMoveNodeId.Value, pendingMoveTargetId);
                TreeUtils.Sort(this.Ui.Plugin.Config.Plates);
                this.Ui.Plugin.SaveConfig();
            }

            if (deferredActions.Count > 0) {
                foreach (var (nodeId, folderAction) in deferredActions) {
                    if (folderAction == null) {
                        // Delete plate node
                        TreeUtils.RemoveNode(this.Ui.Plugin.Config.Plates, nodeId);

                        // If we deleted the selected plate, clear selection
                        if (_selectedPlateId == nodeId) {
                            _selectedPlateId = null;
                        }
                    } else if (folderAction == 0) {
                        // Delete folder and all contents
                        TreeUtils.DeleteFolder(this.Ui.Plugin.Config.Plates, nodeId, false);
                    } else if (folderAction == 1) {
                        // Delete folder but move contents out
                        TreeUtils.DeleteFolder(this.Ui.Plugin.Config.Plates, nodeId, true);
                    }
                }

                TreeUtils.Sort(this.Ui.Plugin.Config.Plates);
                this.Ui.Plugin.SaveConfig();
            }

            ImGui.Separator();

            // Button bar for adding new plates or folders
            ImGui.BeginGroup();
            ImGui.PushItemWidth(-1);
            if (ImGui.Button("+ New Folder")) {
                ImGui.OpenPopup("new-folder-popup");
                _newFolderName = string.Empty;
            }

            ImGui.PopItemWidth();
            if (ImGui.BeginPopup("new-folder-popup")) {
                ImGui.TextUnformatted("Folder Name:");
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputText("##new-folder-name", ref _newFolderName, 128, ImGuiInputTextFlags.EnterReturnsTrue)) {
                    if (!string.IsNullOrWhiteSpace(_newFolderName)) {
                        this.Ui.Plugin.Config.Plates.Add(new FolderNode {
                            Name = _newFolderName.Trim(),
                            Children = new List<TreeNode>()
                        });
                        TreeUtils.Sort(this.Ui.Plugin.Config.Plates);
                        this.Ui.Plugin.SaveConfig();
                        ImGui.CloseCurrentPopup();
                    }
                }

                if (ImGui.Button("Create") && !string.IsNullOrWhiteSpace(_newFolderName)) {
                    this.Ui.Plugin.Config.Plates.Add(new FolderNode {
                        Name = _newFolderName.Trim(),
                        Children = new List<TreeNode>()
                    });
                    TreeUtils.Sort(this.Ui.Plugin.Config.Plates);
                    this.Ui.Plugin.SaveConfig();
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel")) {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            ImGui.EndGroup();
            ImGui.EndChild();
        }

        private void DrawDyePopup(string dyePopup, SavedGlamourItem mirage) {
            if (!ImGui.BeginPopup(dyePopup)) {
                return;
            }

            ImGui.PushItemWidth(-1);
            ImGui.InputText("##dye-filter", ref this._dyeFilter, 512);

            if (ImGui.IsWindowAppearing()) {
                ImGui.SetKeyboardFocusHere(-1);
            }

            ImGui.TextUnformatted("Primary Dye");
            ImGui.NewLine();

            if (ImGui.BeginChild("dye 1 picker", new Vector2(250, 350), false, ImGuiWindowFlags.HorizontalScrollbar)) {
                if (ImGui.Selectable("None", mirage.Stain1 == 0)) {
                    mirage.Stain1 = 0;
                    ImGui.CloseCurrentPopup();
                }

                var filter = this._dyeFilter.ToLowerInvariant();

                foreach (var stain in Service.DataManager.GetExcelSheet<Stain>()!) {
                    if (stain.RowId == 0 || stain.Shade == 0) {
                        continue;
                    }

                    if (filter.Length > 0 && !stain.Name.ExtractText().ToLowerInvariant().Contains(filter)) {
                        continue;
                    }

                    if (ImGui.Selectable($"{stain.Name}##{stain.RowId}", mirage.Stain1 == stain.RowId)) {
                        mirage.Stain1 = (byte) stain.RowId;
                        ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.EndChild();
            }

            if (_editingItemDyeCount == 2) {
                ImGui.NewLine();
                ImGui.Separator();
                ImGui.NewLine();

                ImGui.TextUnformatted("Secondary Dye");
                ImGui.NewLine();

                if (ImGui.BeginChild("dye 2 picker", new Vector2(250, 350), false, ImGuiWindowFlags.HorizontalScrollbar)) {
                    if (ImGui.Selectable("None", mirage.Stain2 == 0)) {
                        mirage.Stain2 = 0;
                        ImGui.CloseCurrentPopup();
                    }

                    var filter = this._dyeFilter.ToLowerInvariant();

                    foreach (var stain in Service.DataManager.GetExcelSheet<Stain>()!) {
                        if (stain.RowId == 0 || stain.Shade == 0) {
                            continue;
                        }

                        if (filter.Length > 0 && !stain.Name.ExtractText().ToLowerInvariant().Contains(filter)) {
                            continue;
                        }

                        if (ImGui.Selectable($"{stain.Name}##{stain.RowId}", mirage.Stain2 == stain.RowId)) {
                            mirage.Stain2 = (byte) stain.RowId;
                            ImGui.CloseCurrentPopup();
                        }
                    }

                    ImGui.EndChild();
                }
            }

            ImGui.EndPopup();
        }

        private unsafe void DrawItemPopup(string itemPopup, SavedPlate plate, PlateSlot slot) {
            if (!ImGui.BeginPopup(itemPopup)) {
                return;
            }

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##item-filter", "Search...", ref this._itemFilter, 512, ImGuiInputTextFlags.AutoSelectAll)) {
                this.FilterItems(slot);
            }

            if (ImGui.IsWindowAppearing()) {
                ImGui.SetKeyboardFocusHere(-1);
            }

            if (GameFunctions.DresserContents.Count > 0) {
                if (ImGui.Checkbox("Only show items in the armoire/dresser", ref this.Ui.Plugin.Config.ItemFilterShowObtainedOnly)) {
                    this.Ui.Plugin.SaveConfig();
                    this.FilterItems(slot);
                }

                ImGui.Separator();
            }

            if (ImGui.BeginChild("item search", new Vector2(250, 450), false, ImGuiWindowFlags.HorizontalScrollbar)) {
                uint? id;
                if (plate.Items.TryGetValue(slot, out var slotMirage)) {
                    id = slotMirage.ItemId;
                } else {
                    id = null;
                }

                if (ImGui.Selectable("##none-keep", id == null)) {
                    plate.Items.Remove(slot);
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                Util.TextIcon(FontAwesomeIcon.Box);
                ImGui.SameLine();
                ImGui.TextUnformatted("None (keep existing)");

                if (ImGui.Selectable("##none-remove)", id == 0)) {
                    plate.Items[slot] = new SavedGlamourItem();
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                Util.TextIcon(FontAwesomeIcon.Box);
                ImGui.SameLine();
                ImGui.TextUnformatted("None (remove existing)");

                var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());

                clipper.Begin(this.FilteredItems.Count);
                while (clipper.Step()) {
                    for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++) {
                        var item = this.FilteredItems[i];

                        if (ImGui.Selectable($"##{item.RowId}", item.RowId == id)) {
                            if (!plate.Items.ContainsKey(slot)) {
                                plate.Items[slot] = new SavedGlamourItem();
                            }

                            plate.Items[slot].ItemId = item.RowId;
                            if (item.DyeCount == 0) {
                                plate.Items[slot].Stain1 = 0;
                                plate.Items[slot].Stain2 = 0;
                            }

                            ImGui.CloseCurrentPopup();
                        }

                        if (Util.IsItemMiddleOrCtrlClicked()) {
                            this.Ui.AlternativeFinders.Add(new AlternativeFinder(this.Ui, item));
                        }

                        ImGui.SameLine();

                        var has = GameFunctions.DresserContents.Any(saved => saved.ItemId % Util.HqItemOffset == item.RowId) || this.Ui.Plugin.Functions.IsInArmoire(item.RowId);

                        if (!has) {
                            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int) ImGuiCol.TextDisabled]);
                        }

                        Util.TextIcon(FontAwesomeIcon.Box);

                        if (!has) {
                            ImGui.PopStyleColor();
                        }

                        ImGui.SameLine();

                        ImGui.TextUnformatted($"{item.Name}");
                    }
                }

                ImGui.EndChild();
            }

            ImGui.EndPopup();
        }

        private unsafe void DrawIcon(PlateSlot slot, SavedPlate plate, int iconSize, int paddingSize) {
            var drawCursor = ImGui.GetCursorScreenPos();
            var tooltip = slot.Name();
            ImGui.BeginGroup();

            plate.Items.TryGetValue(slot, out var mirage);

            var borderColour = *ImGui.GetStyleColorVec4(ImGuiCol.Border);

            // check for item
            if (mirage != null && mirage.ItemId != 0 && GameFunctions.DresserContents.Count > 0) {
                var has = GameFunctions.DresserContents.Any(saved => saved.ItemId % Util.HqItemOffset == mirage.ItemId) || this.Ui.Plugin.Functions.IsInArmoire(mirage.ItemId);
                if (!has) {
                    borderColour = ImGuiColors.DalamudYellow;
                }
            }

            ImGui.GetWindowDrawList().AddRect(drawCursor, drawCursor + new Vector2(iconSize + paddingSize), ImGui.ColorConvertFloat4ToU32(borderColour));

            var cursorBefore = ImGui.GetCursorPos();
            ImGui.InvisibleButton($"preview {slot}", new Vector2(iconSize + paddingSize));
            var cursorAfter = ImGui.GetCursorPos();

            if (mirage != null && mirage.ItemId != 0) {
                if (Service.DataManager.GetExcelSheet<Item>()!.TryGetRow(mirage.ItemId, out var item)) {
                    tooltip += $"\n{item.Name}";

                    var icon = this.Ui.GetIcon(item.Icon);
                    if (icon != null) {
                        ImGui.SetCursorPos(cursorBefore + new Vector2(paddingSize / 2f));
                        ImGui.Image(icon.ImGuiHandle, new Vector2(iconSize));
                        ImGui.SetCursorPos(cursorAfter);

                        var circleCentre = drawCursor + new Vector2(iconSize, 4 + paddingSize / 2f);
                        if (mirage.Stain1 != 0) {
                            if (Service.DataManager.GetExcelSheet<Stain>()!.TryGetRow(mirage.Stain1, out var stain)) {
                                var colour = stain.Color;
                                var abgr = 0xFF000000;
                                abgr |= (colour & 0xFF) << 16;
                                abgr |= ((colour >> 8) & 0xFF) << 8;
                                abgr |= (colour >> 16) & 0xFF;
                                ImGui.GetWindowDrawList().AddCircleFilled(circleCentre, 4, abgr);

                                tooltip += $"\n{stain.Name}";
                            } else
                                tooltip += $"\n(?)";
                        } else {
                            tooltip += $"\n(no primary dye)";

                            if (item.DyeCount != 0)
                                ImGui.GetWindowDrawList().AddCircle(circleCentre, 5, 0xFF000000);
                        }

                        circleCentre = drawCursor + new Vector2(iconSize, 16 + paddingSize / 2f);
                        if (mirage.Stain2 != 0) {
                            if (Service.DataManager.GetExcelSheet<Stain>()!.TryGetRow(mirage.Stain2, out var stain)) {
                                var colour = stain.Color;
                                var abgr = 0xFF000000;
                                abgr |= (colour & 0xFF) << 16;
                                abgr |= ((colour >> 8) & 0xFF) << 8;
                                abgr |= (colour >> 16) & 0xFF;
                                ImGui.GetWindowDrawList().AddCircleFilled(circleCentre, 4, abgr);

                                tooltip += $"\n{stain.Name}";
                            } else
                                tooltip += $"\n(?)";
                        } else {
                            if (item.DyeCount == 2)
                                ImGui.GetWindowDrawList().AddCircle(circleCentre, 5, 0xFF000000);
                        }

                        tooltip += "\n\n* Left click to copy name to clipboard";
                    }
                }
            } else if (mirage != null) {
                // remove
                ImGui.GetWindowDrawList().AddLine(
                    drawCursor + new Vector2(paddingSize / 2f),
                    drawCursor + new Vector2(paddingSize / 2f) + new Vector2(iconSize),
                    ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int) ImGuiCol.Text])
                );

                ImGui.GetWindowDrawList().AddLine(
                    drawCursor + new Vector2(paddingSize / 2f) + new Vector2(iconSize, 0),
                    drawCursor + new Vector2(paddingSize / 2f) + new Vector2(0, iconSize),
                    ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int) ImGuiCol.Text])
                );
            }

            ImGui.EndGroup();

            // fix spacing
            ImGui.SetCursorPos(cursorAfter);

            if (ImGui.IsItemHovered()) {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(tooltip);
                ImGui.EndTooltip();
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !this._editing && mirage != null) {
                if (Service.DataManager.GetExcelSheet<Item>()!.TryGetRow(mirage!.ItemId, out var item)) {
                    string name = item.Name.ExtractText();
                    ImGui.SetClipboardText(name);
                    Service.ChatGui.Print($"Copied '{name}' to clipboard.");
                }
            }

            var itemPopup = $"plate item edit {slot}";
            var dyePopup = $"plate item dye {slot}";
            if (this._editing && ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                ImGui.OpenPopup(itemPopup);
                this.FilterItems(slot);
            }

            if (this._editing && ImGui.IsItemClicked(ImGuiMouseButton.Right) && mirage != null) {
                bool ok = Service.DataManager.GetExcelSheet<Item>()!.TryGetRow(mirage.ItemId, out var itemData);
                var dyeable = ok && itemData.DyeCount != 0;
                if (dyeable) {
                    _editingItemDyeCount = itemData!.DyeCount;
                    ImGui.OpenPopup(dyePopup);
                }
            }

            if (mirage != null && mirage.ItemId != 0 && Util.IsItemMiddleOrCtrlClicked()) {
                if (Service.DataManager.GetExcelSheet<Item>()!.TryGetRow(mirage.ItemId, out var item)) {
                    this.Ui.AlternativeFinders.Add(new AlternativeFinder(this.Ui, item));
                }
            }

            this.DrawItemPopup(itemPopup, plate, slot);

            if (mirage != null) {
                this.DrawDyePopup(dyePopup, mirage);
            }
        }

        private void DrawPlatePreview(SavedPlate plate) {
            const int paddingSize = 12;

            if (!ImGui.BeginTable("plate item preview", 2, ImGuiTableFlags.SizingFixedFit)) {
                return;
            }

            ImGui.TableNextRow();
            foreach (var (left, right) in LeftSide.Zip(RightSide)) {
                ImGui.TableNextColumn();
                this.DrawIcon(left, plate, IconSize, paddingSize);
                ImGui.TableNextColumn();
                this.DrawIcon(right, plate, IconSize, paddingSize);
            }

            ImGui.EndTable();
        }

        private void DrawPlateButtons(PlateNode node) {
            var plate = node.Plate!;
            if (this._editing || !ImGui.BeginTable("plate buttons", 6, ImGuiTableFlags.SizingFixedFit)) {
                return;
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.Check, tooltip: "Apply")) {
                if (!Util.IsEditingPlate(Service.GameGui)) {
                    this.AddTimedMessage("The in-game plate editor must be open.");
                } else {
                    this.Ui.Plugin.Functions.LoadPlate(plate);
                }
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.Search, tooltip: "Try on")) {
                this.Ui.TryOnPlate(plate);
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.Font, tooltip: "Rename")) {
                this._showRename ^= true;
                this._renameInput = plate.Name;
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.PencilAlt, tooltip: "Edit")) {
                this._editing = true;
                this._editingPlate = plate.Clone();
                this._editingPlateId = node.Id;
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.ShareAltSquare, tooltip: "Share")) {
                ImGui.SetClipboardText(JsonConvert.SerializeObject(new SharedPlate(plate)));
                this.AddTimedMessage("Copied to clipboard.");
            }

            ImGui.TableNextColumn();
            if (Util.IconButton(FontAwesomeIcon.FileExport, tooltip: "Export as Text")) {
                ImGui.SetClipboardText(ConvertToText(plate));
                this.AddTimedMessage("Copied to clipboard.");
            }

            ImGui.EndTable();
        }

        private void DrawPlateTags(SavedPlate plate) {
            if (this._editing) {
                return;
            }

            if (!ImGui.CollapsingHeader($"Tags ({plate.Tags.Count})###plate-tags")) {
                return;
            }

            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##tag-input", "Input a tag and press Enter", ref this._tagInput, 128, ImGuiInputTextFlags.EnterReturnsTrue)) {
                if (!string.IsNullOrWhiteSpace(this._tagInput)) {
                    var tag = this._tagInput.Trim();

                    if (!plate.Tags.Contains(tag)) {
                        plate.Tags.Add(tag);
                        plate.Tags.Sort();
                        this.Ui.Plugin.SaveConfig();
                    }
                }

                this._tagInput = string.Empty;
            }

            if (ImGui.BeginChild("tag-list")) {
                var toRemove = -1;
                for (var i = 0; i < plate.Tags.Count; i++) {
                    var tag = plate.Tags[i];

                    if (Util.IconButton(FontAwesomeIcon.Times, $"remove-tag-{i}")) {
                        toRemove = i;
                    }

                    ImGui.SameLine();
                    ImGui.TextUnformatted(tag);
                }

                if (toRemove > -1) {
                    plate.Tags.RemoveAt(toRemove);
                    plate.Tags.Sort();
                    this.Ui.Plugin.SaveConfig();
                }

                ImGui.EndChild();
            }
        }

        private void DrawPlateDetail() {
            if (!ImGui.BeginChild("plate detail")) {
                return;
            }

            if (_selectedPlateId == null) {
                ImGui.EndChild();
                return;
            }

            var node = TreeUtils.FindNodeById(this.Ui.Plugin.Config.Plates, (Guid) _selectedPlateId!);
            if (node is not PlateNode selectedNode) {
                ImGui.EndChild();
                return;
            }

            var plate = _editingPlate ?? selectedNode.Plate;

            {
                ImGui.BeginGroup();
                this.DrawPlatePreview(plate);
                ImGui.EndGroup();
            }

            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10);

            {
                ImGui.BeginGroup();
                this.DrawRightPanel(plate);
                ImGui.EndGroup();
            }

            var renameWasVisible = this._showRename;

            this.DrawPlateButtons(selectedNode);

            foreach (var (msg, _) in this._timedMessages) {
                Util.TextUnformattedWrapped(msg);
            }

            if (this._showRename && Util.DrawTextInput("plate-rename", ref this._renameInput, flags: ImGuiInputTextFlags.AutoSelectAll)) {
                plate.Name = this._renameInput;
                this.Ui.Plugin.SaveConfig();
                this._showRename = false;
            }

            if (this._showRename && !renameWasVisible) {
                ImGui.SetKeyboardFocusHere(-1);
            }

            if (this._editing) {
                Util.TextUnformattedWrapped("Click an item to edit it. Right-click to dye.");

                if (ImGui.Button("Save") && this._editingPlate != null) {
                    selectedNode.Plate = _editingPlate.Clone();

                    TreeUtils.Sort(this.Ui.Plugin.Config.Plates);
                    this.Ui.Plugin.SaveConfig();
                    this.ResetEditing();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel")) {
                    this.ResetEditing();
                }
            }

            this.DrawPlateTags(plate);

            ImGui.EndChild();
        }

        private void DrawRightPanel(SavedPlate plate) {
            // Something broke ImGui separator positioning or I'm an idiot
            // so we're just going to draw our own heading separators
            var drawList = ImGui.GetWindowDrawList();
            void SeparatorHack(int? width = null) {
                var cursorPos = ImGui.GetCursorScreenPos();
                drawList.AddLine(cursorPos + new Vector2(0, -3), cursorPos + new Vector2(width ?? ImGui.GetContentRegionAvail().X, -3), ImGui.GetColorU32(ImGuiCol.Separator));
            }

            ImGui.TextUnformatted(plate.Name);
            SeparatorHack();

            DrawDyeListLabel(plate);

            ImGui.NewLine();
            ImGui.TextUnformatted("Customize");

            bool fillSlots = ImGui.Button("Fill Empty Slots with New Emperor");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Fills empty slots with the New Emperor set.");

            if (fillSlots)
                this.FillEmptySlots(plate);

            if (Interop.Glamourer.IsAvailable()) {
                ImGui.NewLine();
                ImGui.TextUnformatted("Glamourer");
                SeparatorHack();

                if (ImGui.Button("Try On") && Service.ClientState.LocalPlayer != null)
                    Interop.Glamourer.TryOn(Service.ClientState.LocalPlayer!.ObjectIndex, plate);
            }

            ImGui.NewLine();
            ImGui.TextUnformatted("Settings");
            SeparatorHack();

            bool newEmperor = plate.FillWithNewEmperor;
            if (ImGui.Checkbox("Fill empty slots with New Emperor for Try on & Apply", ref newEmperor)) {
                plate.FillWithNewEmperor = newEmperor;
                this.Ui.Plugin.SaveConfig();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Does not modify the glamour itself!\n\nIf enabled, empty slots will be filled with the New Emperor set when tried on or applied to plates.");
        }

        private void DrawDyeListLabel(SavedPlate plate) {
            bool showDyes = false;
            bool copyDyes = false;
            Util.TextIcon(FontAwesomeIcon.Mouse);
            showDyes |= ImGui.IsItemHovered();
            copyDyes |= ImGui.IsItemClicked(ImGuiMouseButton.Left);

            ImGui.SameLine();

            ImGui.TextUnformatted("Hover to view dyes (click to copy)");
            showDyes |= ImGui.IsItemHovered();
            copyDyes |= ImGui.IsItemClicked(ImGuiMouseButton.Left);

            if (showDyes) {
                Dictionary<byte, int> dyes = [];
                foreach (var (_, item) in plate.Items) {
                    if (item.Stain1 != 0)
                        dyes[item.Stain1] = (dyes.ContainsKey(item.Stain1) ? dyes[item.Stain1] : 0) + 1;

                    if (item.Stain2 != 0)
                        dyes[item.Stain2] = (dyes.ContainsKey(item.Stain2) ? dyes[item.Stain2] : 0) + 1;
                }

                StringBuilder export = new();

                ImGui.BeginTooltip();

                foreach (var (dye, count) in dyes.OrderBy(kvp => kvp.Key)) {
                    if (Service.DataManager.GetExcelSheet<Stain>()!.TryGetRow(dye, out var stain)) {
                        string line = $"{count}x {stain.Name}";
                        ImGui.TextUnformatted(line);

                        if (copyDyes)
                            export.AppendLine(line);
                    }
                }

                if (copyDyes || DateTime.Now.Subtract(_dyesCopiedTime).TotalSeconds < 1.5) {
                    ImGui.NewLine();
                    ImGui.TextUnformatted("Copied to clipboard!");
                }

                ImGui.EndTooltip();

                if (copyDyes) {
                    ImGui.SetClipboardText(export.ToString().Substring(0, export.Length - 2));
                    _dyesCopiedTime = DateTime.Now;
                }
            }
        }

        private void DrawWarnings() {
            var warnings = new List<string>();

            if (!this.Ui.Plugin.Functions.ArmoireLoaded) {
                warnings.Add("The Armoire is not loaded. Open it once to enable glamours from the Armoire.");
            }

            if (GameFunctions.DresserContents.Count == 0) {
                warnings.Add("Glamour Dresser is empty or has not been opened. Glamaholic will not know which items you have.");
            }

            if (warnings.Count == 0) {
                return;
            }

            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            var header = ImGui.CollapsingHeader($"Warnings ({warnings.Count})###warnings");
            ImGui.PopStyleColor();

            if (!header) {
                return;
            }

            for (var i = 0; i < warnings.Count; i++) {
                if (i != 0) {
                    ImGui.Separator();
                }

                Util.TextUnformattedWrapped(warnings[i]);
            }
        }

        private void DrawInner() {
            this.DrawMenuBar();

            this.DrawWarnings();

            this.DrawPlateList();

            ImGui.SameLine();

            this.DrawPlateDetail();
        }

        private void DrawMassImportWindow() {
            var displaySize = ImGui.GetIO().DisplaySize;
            var windowSize = new Vector2(500, 300);
            var windowPos = (displaySize - windowSize) / 2;

            ImGui.SetNextWindowPos(windowPos, ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.Appearing);

            if (!ImGui.Begin("Mass Import", ref this._massImport)) {
                ImGui.End();
                return;
            }

            ImGui.TextUnformatted("Eorzea Collection Mass Import");
            ImGui.Separator();

            {
                ImGui.TextUnformatted("Target:");
                ImGui.RadioButton("New Plate", ref this._massImportTarget, 0); ImGui.SameLine();
                ImGui.RadioButton("Try On", ref this._massImportTarget, 1);

                if (Interop.Glamourer.IsAvailable()) {
                    ImGui.SameLine();
                    ImGui.RadioButton("Try On (Glamourer)", ref this._massImportTarget, 2);
                }
            }

            var target = (ECImportTarget) this._massImportTarget;

            if (!this._ecImporting) {
                var clipboard = Util.GetClipboardText().Trim();
                if (clipboard != _massImportLastURL && IsValidEorzeaCollectionUrl(clipboard)) {
                    _massImportLastURL = clipboard;
                    _massImportMessage = "Importing...";
                    ImportEorzeaCollection(clipboard, target);
                }
            }

            ImGui.NewLine();
            ImGui.TextUnformatted("Copy an Eorzea Collection URL to import it.");

            ImGui.NewLine();
            if (this._ecImporting)
                ImGui.TextUnformatted("Importing...");
            else
                ImGui.TextUnformatted(_massImportMessage);

            ImGui.SetCursorPosY(ImGui.GetWindowHeight() - ImGui.GetFrameHeightWithSpacing() - ImGui.GetStyle().ItemSpacing.Y);
            if (ImGui.Button("Close")) {
                this._massImport = false;
            }
        }

        private void HandleTimers() {
            if (this._timedMessages.Count == 0) return;

            var keys = this._timedMessages.Keys.ToArray();
            foreach (var key in keys) {
                if (this._timedMessages[key].Elapsed > TimeSpan.FromSeconds(5)) {
                    this._timedMessages.Remove(key);
                }
            }
        }

        private void AddTimedMessage(string message) {
            var timer = new Stopwatch();
            timer.Start();
            this._timedMessages[message] = timer;
        }

        internal void SwitchPlate(Guid plateId, bool scrollTo = false) {
            this._selectedPlateId = plateId;
            this._scrollToSelected = scrollTo;
            this._renameInput = string.Empty;
            this._showRename = false;
            this._deleteConfirm = false;
            this._timedMessages.Clear();
            this.ResetEditing();
        }

        private void ResetEditing() {
            this._editing = false;
            this._editingPlate = null;
            this._editingPlateId = null;
            this._itemFilter = string.Empty;
            this._dyeFilter = string.Empty;
        }

        private void FilterItems(PlateSlot slot) {
            var filter = this._itemFilter.ToLowerInvariant();

            IEnumerable<Item> items;
            if (GameFunctions.DresserContents.Count > 0 && this.Ui.Plugin.Config.ItemFilterShowObtainedOnly) {
                var sheet = Service.DataManager.GetExcelSheet<Item>()!;
                items = GameFunctions.DresserContents
                    .Select(item => sheet.GetRowOrDefault(item.ItemId))
                    .Where(item => item != null)
                    .Cast<Item>();
            } else {
                items = DataCache.EquippableItems.Value;
            }

            this.FilteredItems = items
                .Where(item => !Util.IsItemSkipped(item))
                .Where(item => Util.MatchesSlot(item.EquipSlotCategory.Value!, slot))
                .Where(item => this._itemFilter.Length == 0 || item.Name.ExtractText().ToLowerInvariant().Contains(filter))
                .ToImmutableList();
        }

        private void FillEmptySlots(SavedPlate plate) {
            foreach (var slot in Enum.GetValues<PlateSlot>()) {
                if (plate.Items.ContainsKey(slot))
                    continue;

                uint emperor = Util.GetEmperorItemForSlot(slot);
                if (emperor == 0)
                    continue;

                plate.Items[slot] = new SavedGlamourItem {
                    ItemId = emperor,
                    Stain1 = 0,
                    Stain2 = 0,
                };
            }

            this.Ui.Plugin.SaveConfig();
        }

        private string ConvertToText(SavedPlate plate) {
            var stainSheet = Service.DataManager.GetExcelSheet<Stain>()!;

            StringBuilder sb = new();
            sb.AppendLine(plate.Name);
            sb.AppendLine("---");
            foreach (var kvp in plate.Items) {
                if (kvp.Value.ItemId == 0) {
                    continue;
                } else {
                    sb.Append(kvp.Key.ToString()).Append(": ");

                    if (!DataCache.EquippableItems.Value.TryGetFirst(i => i.RowId == kvp.Value.ItemId, out Item item)) {
                        sb.AppendLine("Unknown Item");
                    } else {
                        sb.Append(item.Name);

                        bool hasStain = kvp.Value.Stain1 != 0 || kvp.Value.Stain2 != 0;
                        if (!hasStain) {
                            sb.AppendLine();
                            continue;
                        }

                        sb.Append(" (");
                        if (kvp.Value.Stain1 != 0) {
                            if (!stainSheet.TryGetRow(kvp.Value.Stain1, out var stain)) {
                                sb.Append("Unknown Stain");
                            } else {
                                sb.Append(stain.Name);
                            }
                        } else
                            sb.Append("-, ");

                        if (kvp.Value.Stain2 != 0) {
                            if (kvp.Value.Stain1 != 0)
                                sb.Append(", ");

                            if (!stainSheet.TryGetRow(kvp.Value.Stain2, out var stain)) {
                                sb.Append("Unknown Stain");
                            } else {
                                sb.Append(stain.Name);
                            }
                        }

                        sb.AppendLine(")");
                    }
                }
            }

            return sb.ToString();
        }

        private bool IsDescendantOf(List<TreeNode> rootNodes, Guid potentialDescendantId, Guid ancestorId) {
            var ancestorNode = TreeUtils.FindNodeById(rootNodes, ancestorId);
            if (ancestorNode is not FolderNode ancestorFolder)
                return false;

            return IsDescendantOfRecursive(ancestorFolder.Children, potentialDescendantId);
        }

        private bool IsDescendantOfRecursive(List<TreeNode> nodes, Guid targetId) {
            foreach (var node in nodes) {
                if (node.Id == targetId)
                    return true;

                if (node is FolderNode folder && IsDescendantOfRecursive(folder.Children, targetId))
                    return true;
            }
            return false;
        }
    }
}
