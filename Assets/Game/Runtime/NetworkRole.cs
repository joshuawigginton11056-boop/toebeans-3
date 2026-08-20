namespace Toebeans.Game
{
    /// <summary>
    /// Who this process is in the session. There is no netcode in the project yet, so today every
    /// session is <see cref="Offline"/> - but every mutation in <see cref="GameDirector"/> already
    /// asks this before it writes.
    ///
    /// That is the whole point of having it early. Authority checks are cheap to write now and
    /// miserable to retrofit: without them, UI code grows the habit of poking session state
    /// directly, and each of those pokes becomes a desync the day a second machine connects.
    /// </summary>
    public enum NetworkRole
    {
        /// <summary>Single machine, no transport. Authoritative over itself.</summary>
        Offline = 0,

        /// <summary>Owns the session state and is the only one allowed to change it.</summary>
        Host = 1,

        /// <summary>Mirrors the host's state and may only ask for changes, never make them.</summary>
        Client = 2,
    }
}
