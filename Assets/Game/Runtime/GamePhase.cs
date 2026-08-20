namespace Toebeans.Game
{
    /// <summary>
    /// Where the application is. This is deliberately NOT the same axis as
    /// <see cref="GameModeDefinition"/>: the phase is which screen owns the player right now,
    /// the mode is the ruleset they picked to play once they get there. "Grand Prix" is a mode;
    /// "sitting in the lobby choosing one" is a phase.
    ///
    /// The values are written down explicitly because a phase is the first thing a host will
    /// send a joining client, and renumbering an enum under a live build silently teleports
    /// people into the wrong screen.
    /// </summary>
    public enum GamePhase
    {
        /// <summary>First frame only, before <see cref="GameDirector"/> has decided anything.</summary>
        Boot = 0,

        /// <summary>Title screen. No session exists yet - this is the one phase with no roster.</summary>
        MainMenu = 1,

        /// <summary>A session exists and racers are gathering. Mode is chosen here.</summary>
        Lobby = 2,

        /// <summary>Choosing which track to race. Split from the lobby so voting can slot in later.</summary>
        TrackSelect = 3,

        /// <summary>Track scene is loading. Exists as its own phase so the UI has somewhere to live
        /// while the lobby scene is gone and the race scene has not arrived.</summary>
        Loading = 4,

        /// <summary>On the track. Hand-off point to RaceSession, which owns everything past here.</summary>
        Race = 5,

        /// <summary>Standings after the flag, before going back round to the lobby.</summary>
        Results = 6,
    }
}
