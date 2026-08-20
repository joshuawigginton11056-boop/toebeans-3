using System;
using UnityEngine;

namespace Toebeans.Game
{
    /// <summary>
    /// One seat on the grid. Plain serializable data on purpose - no GameObject references, no
    /// scene objects, nothing that only means something inside this process. A slot has to survive
    /// being packed into a message and rebuilt on another machine, so everything it points at is
    /// named by a stable id that both ends can look up for themselves.
    ///
    /// The same struct-of-data describes a person, a peer and a bot; <see cref="Kind"/> is the only
    /// thing that differs. Code downstream of the lobby should almost never branch on it.
    /// </summary>
    [Serializable]
    public sealed class RacerSlot
    {
        [Tooltip("Stable identity for this racer, unique within the session. Survives the whole session.")]
        public string racerId = string.Empty;

        [Tooltip("Name shown on the grid and the results board.")]
        public string displayName = string.Empty;

        public RacerKind kind = RacerKind.Empty;

        [Tooltip("Which connected machine owns this seat. 0 is the host. Meaningless while offline, " +
                 "but carried now so the field does not have to be threaded through later.")]
        public ulong ownerClientId;

        [Tooltip("Index into the local players on the owning machine - 0 unless that machine is " +
                 "running split-screen. Together with ownerClientId this identifies a gamepad.")]
        public int localPlayerIndex;

        [Tooltip("Chosen character, by id. Empty means the game picks one.")]
        public string characterId = string.Empty;

        [Tooltip("Chosen kart, by id. Empty means the game picks one.")]
        public string kartId = string.Empty;

        [Tooltip("Team for team modes. -1 is no team.")]
        public int teamIndex = -1;

        [Tooltip("Ready to start. The host will not leave the lobby until every non-empty seat is ready.")]
        public bool isReady;

        [Tooltip("How hard this racer drives, 0 to 1. Only read for AI racers, and only once they exist.")]
        [Range(0f, 1f)]
        public float aiSkill = 0.5f;

        /// <summary>A seat with somebody in it - a person, a peer or a bot.</summary>
        public bool IsOccupied { get { return kind != RacerKind.Empty; } }

        /// <summary>Driven from this machine, so this process owns its input and camera.</summary>
        public bool IsLocal { get { return kind == RacerKind.LocalPlayer; } }

        /// <summary>
        /// Whether this seat has to press ready itself. Bots have no way to, so they never hold
        /// the countdown up - without this, one AI racer means the lobby can never start.
        /// </summary>
        public bool RequiresReadyUp { get { return kind == RacerKind.LocalPlayer || kind == RacerKind.RemotePlayer; } }

        public RacerSlot Clone()
        {
            return (RacerSlot)MemberwiseClone();
        }

        public override string ToString()
        {
            return string.Format("{0} [{1}]{2}", displayName, kind, isReady ? " ready" : string.Empty);
        }
    }
}
