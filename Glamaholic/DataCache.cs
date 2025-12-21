using Dalamud.Game;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Glamaholic {
    // Lazy loading and caching of important data which requires filtering or transforming
    internal class DataCache {
        public static Lazy<ImmutableList<Item>> EquippableItems { get; } =
            new(() => Service.DataManager.GetExcelSheet<Item>(ClientLanguage.English)!
                .Where(row => row.EquipSlotCategory.RowId != 0 &&
                       row.EquipSlotCategory.Value!.SoulCrystal == 0)
                .ToImmutableList());

        public static Lazy<ImmutableDictionary<string, byte>> StainLookup { get; } =
            new (() =>
                Service.DataManager.GetExcelSheet<Stain>(ClientLanguage.English)!
                    .Where(static row => row.RowId != 0 && !row.Name.IsEmpty)
                    .ToImmutableDictionary(static row =>
                        row.Name.ExtractText().Trim().ToLower(), static row => (byte) row.RowId));

        public static int GetNumStainSlots(uint itemId) =>
            Service.DataManager.GetExcelSheet<Item>(ClientLanguage.English)!.GetRowOrDefault(itemId)?.DyeCount ?? 0;
    }
}
