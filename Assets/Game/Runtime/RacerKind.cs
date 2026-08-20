namespace Toebeans.Game
{
    /// <summary>
    /// What is driving a seat on the grid. The roster is deliberately kind-agnostic everywhere
    /// else: a <see cref="RacerSlot"/> carries a name, a kart and a skill number whether a person,
    /// a remote peer or the AI is behind it.
    ///
    /// Filling an empty lobby with AI is then a roster edit rather than a special mode, which is
    /// the difference between adding bots later and rewriting the lobby later.
    /// </summary>
    public enum RacerKind
    {
        /// <summary>Reserved seat nobody has taken. Never appears on a started grid.</summary>
        Empty = 0,

        /// <summary>A person on this machine. Split-screen means more than one of these.</summary>
        LocalPlayer = 1,

        /// <summary>A person on another machine.</summary>
        RemotePlayer = 2,

        /// <summary>Driven by the AI. Simulated on the host and replicated like any other racer.</summary>
        Ai = 3,
    }
}
