using System;
using System.Collections.Generic;
using UnityEngine;

namespace Toebeans.Game
{
    /// <summary>
    /// Everything a lobby full of people has to agree on: the mode, the track, the rules and who is
    /// on the grid. One object, all of it plain serializable data, all of it addressed by stable
    /// ids rather than object references.
    ///
    /// This is the thing that gets replicated. When netcode lands, the host owns an instance and
    /// clients receive copies; nothing in here may become a reference to a scene object, an asset,
    /// or anything else whose meaning is local to one process. Look things up through
    /// <see cref="GameContent"/> at the point of use instead - <see cref="modeId"/> resolves to a
    /// <see cref="GameModeDefinition"/> the same way on every machine, where a direct reference
    /// would resolve on none of them.
    ///
    /// Queries live here. Changes do not: they go through <see cref="GameDirector"/> so they can be
    /// authority-checked in one place.
    /// </summary>
    [Serializable]
    public sealed class GameSession
    {
        [Tooltip("Identifies this particular session. Regenerated every time a session is opened.")]
        public string sessionId = string.Empty;

        [Tooltip("Chosen ruleset, by id. See GameModeDefinition.")]
        public string modeId = string.Empty;

        [Tooltip("Chosen track, by id. Empty until the players get through track select.")]
        public string trackId = string.Empty;

        [Tooltip("Laps for the next race, or 0 to take the default from the mode and track.")]
        public int lapCountOverride;

        [Tooltip("Seeds every random decision a race makes - item rolls, AI personality, grid order. " +
                 "Held in session state so that host and clients roll the same numbers rather than " +
                 "each drifting off their own Random.")]
        public int raceSeed;

        [Tooltip("Hard ceiling on the grid. The mode narrows this further.")]
        public int maxRacers = 8;

        [Tooltip("Whether the host tops the grid up with AI racers before starting.")]
        public bool aiFillEnabled = true;

        [Tooltip("Grid size to top up to when AI fill is on. Clamped to maxRacers and the mode.")]
        public int aiFillTarget = 8;

        [Tooltip("The grid, in slot order. Order is meaningful - it is the starting order.")]
        public List<RacerSlot> racers = new List<RacerSlot>();

        /// <summary>Seats with somebody in them.</summary>
        public int OccupiedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < racers.Count; i++)
                {
                    if (racers[i].IsOccupied)
                        count++;
                }
                return count;
            }
        }

        /// <summary>Seats driven by a person on this machine. More than one means split-screen.</summary>
        public int LocalCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < racers.Count; i++)
                {
                    if (racers[i].IsLocal)
                        count++;
                }
                return count;
            }
        }

        public int AiCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < racers.Count; i++)
                {
                    if (racers[i].kind == RacerKind.Ai)
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Every human seat has readied up. Bots are skipped rather than auto-readied, so that a
        /// grid of nothing but bots is startable and a single unready person still blocks.
        /// </summary>
        public bool EveryoneReady
        {
            get
            {
                for (int i = 0; i < racers.Count; i++)
                {
                    if (racers[i].RequiresReadyUp && !racers[i].isReady)
                        return false;
                }
                return true;
            }
        }

        public bool HasRoom { get { return OccupiedCount < maxRacers; } }

        public RacerSlot FindRacer(string racerId)
        {
            if (string.IsNullOrEmpty(racerId))
                return null;

            for (int i = 0; i < racers.Count; i++)
            {
                if (racers[i].racerId == racerId)
                    return racers[i];
            }
            return null;
        }

        /// <summary>The first local seat, which is whose input drives the menus.</summary>
        public RacerSlot PrimaryLocalRacer
        {
            get
            {
                for (int i = 0; i < racers.Count; i++)
                {
                    if (racers[i].IsLocal)
                        return racers[i];
                }
                return null;
            }
        }

        /// <summary>
        /// Laps the next race should run: an explicit override if one is set, otherwise whatever
        /// the track asks for, otherwise the mode's default. Track beats mode because lap count is
        /// really a property of how long a lap is.
        /// </summary>
        public int ResolveLapCount(GameModeDefinition mode, TrackDefinition track)
        {
            if (lapCountOverride > 0)
                return lapCountOverride;
            if (track != null && track.defaultLapCount > 0)
                return track.defaultLapCount;
            if (mode != null && mode.defaultLapCount > 0)
                return mode.defaultLapCount;
            return 3;
        }

        /// <summary>Wipes the session back to nothing. Used when the players drop out to the title.</summary>
        public void Clear()
        {
            sessionId = string.Empty;
            modeId = string.Empty;
            trackId = string.Empty;
            lapCountOverride = 0;
            raceSeed = 0;
            racers.Clear();
        }
    }
}
