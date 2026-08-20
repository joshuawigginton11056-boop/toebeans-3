using System.Collections.Generic;
using UnityEngine;

namespace Toebeans.Game
{
    /// <summary>
    /// Every track the game knows about, in picker order. The map select screen reads this and
    /// nothing else, so adding a map is: make a <see cref="TrackDefinition"/>, add it here.
    ///
    /// It also exists so an id can be turned back into a track on a machine that never chose it.
    /// A client told to load "sunset_bay" looks it up in its own copy of this asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Toebeans/Track Catalog", fileName = "TrackCatalog")]
    public sealed class TrackCatalog : ScriptableObject
    {
        [Tooltip("All tracks. Order here is only a fallback - sortOrder on each track wins.")]
        public List<TrackDefinition> tracks = new List<TrackDefinition>();

        /// <summary>Tracks that should appear on the picker, in the order they should appear.</summary>
        public List<TrackDefinition> GetSelectable()
        {
            var result = new List<TrackDefinition>();
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i] != null && tracks[i].IsSelectable)
                    result.Add(tracks[i]);
            }
            result.Sort(CompareTracks);
            return result;
        }

        static int CompareTracks(TrackDefinition a, TrackDefinition b)
        {
            int order = a.sortOrder.CompareTo(b.sortOrder);
            return order != 0 ? order : string.CompareOrdinal(a.displayName, b.displayName);
        }

        public TrackDefinition Find(string trackId)
        {
            if (string.IsNullOrEmpty(trackId))
                return null;

            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i] != null && tracks[i].trackId == trackId)
                    return tracks[i];
            }
            return null;
        }

        /// <summary>First selectable track, used when something has to pick one and nobody has.</summary>
        public TrackDefinition FirstSelectable()
        {
            List<TrackDefinition> selectable = GetSelectable();
            return selectable.Count > 0 ? selectable[0] : null;
        }

        /// <summary>
        /// Ids that appear more than once. Duplicates are the failure mode that matters here:
        /// two tracks answering to one id means the host and a client can disagree about which
        /// map they are loading while both believe they agree.
        /// </summary>
        public List<string> FindDuplicateIds()
        {
            var seen = new HashSet<string>();
            var duplicates = new List<string>();
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i] == null || string.IsNullOrEmpty(tracks[i].trackId))
                    continue;
                if (!seen.Add(tracks[i].trackId) && !duplicates.Contains(tracks[i].trackId))
                    duplicates.Add(tracks[i].trackId);
            }
            return duplicates;
        }
    }
}
