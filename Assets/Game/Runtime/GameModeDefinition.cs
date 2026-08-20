using UnityEngine;

namespace Toebeans.Game
{
    /// <summary>
    /// A ruleset the players can pick in the lobby - Grand Prix, VS Race, Time Trial and whatever
    /// else the game grows. An asset rather than an enum so that adding a mode is a matter of
    /// making one, and so the lobby UI can list modes it was never compiled against.
    ///
    /// Only <see cref="modeId"/> ever crosses the wire. Everything else here is presentation and
    /// rules that both machines read out of their own copy of the same asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Toebeans/Game Mode", fileName = "Mode_New")]
    public sealed class GameModeDefinition : ScriptableObject
    {
        [Tooltip("Stable id, and the only part of this asset that is ever sent to another machine. " +
                 "Renaming the asset is safe; changing this is not.")]
        public string modeId = string.Empty;

        [Tooltip("Name shown in the lobby.")]
        public string displayName = string.Empty;

        [TextArea(2, 4)]
        [Tooltip("One or two lines under the name on the mode picker.")]
        public string description = string.Empty;

        public Sprite icon;

        [Tooltip("Order in the mode list. Lower comes first.")]
        public int sortOrder;

        [Header("Grid")]
        [Tooltip("Fewest racers this mode will start with, counting bots.")]
        [Min(1)]
        public int minRacers = 1;

        [Tooltip("Most racers this mode will start with. The track's own limit narrows it further.")]
        [Min(1)]
        public int maxRacers = 8;

        [Tooltip("Whether the host may top an empty grid up with AI. Off for modes that are " +
                 "meaningless against bots - a time trial has nobody to race.")]
        public bool allowsAiFill = true;

        [Header("Rules")]
        [Tooltip("Laps when the track does not specify. The track wins if it sets its own.")]
        [Min(1)]
        public int defaultLapCount = 3;

        [Tooltip("Whether item boxes spawn. Off makes a mode a pure driving contest.")]
        public bool itemsEnabled = true;

        [Tooltip("Whether the players pick a track before racing. A Grand Prix runs a fixed cup " +
                 "instead, so it skips track select entirely and the phase machine has to know.")]
        public bool usesTrackSelect = true;

        [Tooltip("Whether racers are split into teams.")]
        public bool isTeamMode;

        /// <summary>The mode is set up enough to be offered to players.</summary>
        public bool IsValid
        {
            get { return !string.IsNullOrEmpty(modeId) && maxRacers >= minRacers; }
        }

        /// <summary>Grid size this mode allows, given whatever ceiling the session already has.</summary>
        public int ClampGridSize(int requested)
        {
            return Mathf.Clamp(requested, minRacers, maxRacers);
        }

        void OnValidate()
        {
            if (maxRacers < minRacers)
                maxRacers = minRacers;

            // An id that differs from the asset name by accident is very hard to spot later, so
            // default it from the file the first time rather than shipping an empty one.
            if (string.IsNullOrEmpty(modeId))
                modeId = name;
            if (string.IsNullOrEmpty(displayName))
                displayName = name;
        }
    }
}
