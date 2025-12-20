using System;
using System.Collections.Generic;
using System.Linq;

namespace Glamaholic {
    internal static class Changelog {
        internal record ChangelogEntry(Version Version, string[] Changes);

        internal static readonly List<ChangelogEntry> Entries = [
            new ChangelogEntry(new Version(1, 12, 0, 0), [
                "Folders can now be renamed through the right-click context menu.",
                "Plates and folders can now be dragged and reordered manually, and automatic sorting has been removed.",
                "Improved third-party plugin support.",
            ]),
        ];

        /// <summary>
        /// Gets all changelog entries that are newer than the specified version.
        /// </summary>
        internal static List<ChangelogEntry> GetEntriesSince(Version? lastSeenVersion) {
            if (lastSeenVersion == null) {
                return Entries;
            }

            return Entries
                .Where(e => e.Version > lastSeenVersion)
                .OrderByDescending(e => e.Version)
                .ToList();
        }

        /// <summary>
        /// Gets the latest version from the changelog.
        /// </summary>
        internal static Version? GetLatestVersion() {
            return Entries.Count > 0 
                ? Entries.Max(e => e.Version) 
                : null;
        }
    }
}
