using System.Collections.Generic;
using UnityEngine;

namespace Toebeans.Game
{
    /// <summary>
    /// Every mode the game offers, in lobby order. The counterpart to <see cref="TrackCatalog"/>,
    /// and the same reasoning: the lobby lists what is in here, and an id coming off the wire is
    /// resolved through here.
    /// </summary>
    [CreateAssetMenu(menuName = "Toebeans/Game Mode Catalog", fileName = "GameModeCatalog")]
    public sealed class GameModeCatalog : ScriptableObject
    {
        public List<GameModeDefinition> modes = new List<GameModeDefinition>();

        [Tooltip("Mode a fresh session starts on, by id. Falls back to the first valid mode.")]
        public string defaultModeId = string.Empty;

        public List<GameModeDefinition> GetSelectable()
        {
            var result = new List<GameModeDefinition>();
            for (int i = 0; i < modes.Count; i++)
            {
                if (modes[i] != null && modes[i].IsValid)
                    result.Add(modes[i]);
            }
            result.Sort(CompareModes);
            return result;
        }

        static int CompareModes(GameModeDefinition a, GameModeDefinition b)
        {
            int order = a.sortOrder.CompareTo(b.sortOrder);
            return order != 0 ? order : string.CompareOrdinal(a.displayName, b.displayName);
        }

        public GameModeDefinition Find(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
                return null;

            for (int i = 0; i < modes.Count; i++)
            {
                if (modes[i] != null && modes[i].modeId == modeId)
                    return modes[i];
            }
            return null;
        }

        /// <summary>The mode a new session opens on.</summary>
        public GameModeDefinition Default()
        {
            GameModeDefinition preferred = Find(defaultModeId);
            if (preferred != null)
                return preferred;

            List<GameModeDefinition> selectable = GetSelectable();
            return selectable.Count > 0 ? selectable[0] : null;
        }

        public List<string> FindDuplicateIds()
        {
            var seen = new HashSet<string>();
            var duplicates = new List<string>();
            for (int i = 0; i < modes.Count; i++)
            {
                if (modes[i] == null || string.IsNullOrEmpty(modes[i].modeId))
                    continue;
                if (!seen.Add(modes[i].modeId) && !duplicates.Contains(modes[i].modeId))
                    duplicates.Add(modes[i].modeId);
            }
            return duplicates;
        }
    }
}
