using UnityEngine;

namespace Toebeans.Game
{
    /// <summary>
    /// Turns the ids in <see cref="GameSession"/> back into the assets they name.
    ///
    /// Session state carries ids precisely so it can be sent somewhere else, which leaves every
    /// reader needing the same lookup. Rather than have each screen hold its own catalog
    /// reference - and get it wrong once - the catalogs are bound here by
    /// <see cref="GameDirector"/> at startup and read from here everywhere.
    ///
    /// Static, because content catalogs are genuinely global and immutable at runtime: they are
    /// the same on the title screen, in the lobby, and mid-race, and nothing ever writes to them.
    /// </summary>
    public static class GameContent
    {
        public static GameModeCatalog Modes { get; private set; }
        public static TrackCatalog Tracks { get; private set; }

        public static bool IsBound { get { return Modes != null && Tracks != null; } }

        public static void Bind(GameModeCatalog modes, TrackCatalog tracks)
        {
            Modes = modes;
            Tracks = tracks;
        }

        /// <summary>
        /// Dropped on domain reload so a stale catalog from the previous play session cannot
        /// survive into the next one. With Enter Play Mode Options on, statics are not cleared
        /// for us, and a dangling reference here would be a confusing thing to debug.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnLoad()
        {
            Modes = null;
            Tracks = null;
        }

        public static GameModeDefinition GetMode(string modeId)
        {
            return Modes != null ? Modes.Find(modeId) : null;
        }

        public static TrackDefinition GetTrack(string trackId)
        {
            return Tracks != null ? Tracks.Find(trackId) : null;
        }
    }
}
