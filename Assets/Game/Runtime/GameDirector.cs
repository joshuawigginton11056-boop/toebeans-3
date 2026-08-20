using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Toebeans.Game
{
    /// <summary>
    /// The one object that decides what the game is doing: which <see cref="GamePhase"/> is up,
    /// what is in the <see cref="GameSession"/>, and whether this machine is allowed to change it.
    /// It survives scene loads and there is exactly one.
    ///
    /// The shape here is the part that matters, and it is built for a game that does not have
    /// netcode yet:
    ///
    ///   * <b>Nothing outside this class writes session state.</b> UI calls a Request method and
    ///     watches an event. Today a request validates and applies immediately; on a client it will
    ///     validate, send, and wait. That difference stays inside this file, which is only true if
    ///     no screen ever reached in and set a field directly - so none may, starting now.
    ///   * <b>Every request is authority-checked</b> via <see cref="HasAuthority"/>, even though
    ///     <see cref="NetworkRole.Offline"/> always passes. The checks are the scaffolding; making
    ///     them real later is a one-line change per call site instead of an audit.
    ///   * <b>Phase changes are validated against a table</b> rather than assigned. A client will
    ///     eventually take phase changes from the wire, and a stale or out-of-order message must
    ///     not be able to drop somebody into Results from the title screen.
    ///   * <b>Bots are roster entries.</b> AI fill adds <see cref="RacerKind.Ai"/> slots and changes
    ///     nothing else, so the grid the race builds is one list either way.
    ///
    /// Everything from the green light onwards belongs to RaceSession, not here. This class hands
    /// over a loaded track and a settled grid, and takes the results back.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameDirector : MonoBehaviour
    {
        public static GameDirector Instance { get; private set; }

        [Header("Content")]
        [Tooltip("Every mode the lobby can offer.")]
        public GameModeCatalog modeCatalog;

        [Tooltip("Every track the map picker can offer.")]
        public TrackCatalog trackCatalog;

        [Header("Front end")]
        [Tooltip("Scene holding the title screen and lobby. Loaded when the players leave a race. " +
                 "Leave empty if the front end lives in the same scene as this object.")]
        public string frontEndScenePath = string.Empty;

        [Tooltip("Phase to open on. MainMenu for the real game; set to Lobby to skip the title " +
                 "while working on lobby UI.")]
        public GamePhase startPhase = GamePhase.MainMenu;

        [Header("Local player")]
        [Tooltip("Name the local player appears under until there is a profile system.")]
        public string localPlayerName = "Player 1";

        [Header("AI fill")]
        [Tooltip("Names handed out to AI racers, in order. Runs out gracefully - extras are numbered.")]
        public string[] aiRacerNames =
        {
            "Bramble", "Cinder", "Dune", "Frost", "Gale", "Husk", "Iris", "Jetty",
            "Kite", "Lumen", "Moss", "Nimbus"
        };

        /// <summary>Where the game is right now.</summary>
        public GamePhase Phase { get; private set; }

        /// <summary>What this machine is in the session. Always Offline until netcode exists.</summary>
        public NetworkRole Role { get; private set; }

        /// <summary>The replicated state. Read freely; write only through the Request methods.</summary>
        public GameSession Session { get; private set; }

        /// <summary>
        /// Whether this machine may change session state. A host and an offline game may; a client
        /// may only ask. Every mutating method on this class asks this first.
        /// </summary>
        public bool HasAuthority { get { return Role != NetworkRole.Client; } }

        /// <summary>Track load progress, 0 to 1, meaningful during <see cref="GamePhase.Loading"/>.</summary>
        public float LoadProgress { get; private set; }

        /// <summary>
        /// Declare what this machine is in the session. The transport owns this call - it is made
        /// when a connection is established, lost, or promoted - and nothing else should make it.
        ///
        /// The role decides who may write session state, so a client calling this on itself is
        /// exactly how a client would grant itself authority it has not got. It stays a method
        /// rather than a settable property to keep that visible at the call site.
        /// </summary>
        public void SetRole(NetworkRole role)
        {
            if (Role == role)
                return;

            Role = role;
            RaiseSessionChanged();
        }

        /// <summary>Fired after the phase changes, with the phase left and the phase entered.</summary>
        public event Action<GamePhase, GamePhase> PhaseChanged;

        /// <summary>
        /// Fired whenever anything in <see cref="Session"/> changes. Coarse on purpose: a lobby
        /// rebuilding its roster list on any change is cheap, and one event is far harder to
        /// forget to raise than fifteen.
        /// </summary>
        public event Action SessionChanged;

        /// <summary>Fired when a request is refused, with a reason worth putting on screen.</summary>
        public event Action<string> RequestRejected;

        Coroutine _loadRoutine;

        /// <summary>The mode the session is set to, resolved through the catalog.</summary>
        public GameModeDefinition CurrentMode { get { return GameContent.GetMode(Session.modeId); } }

        /// <summary>The track the session is set to, resolved through the catalog.</summary>
        public TrackDefinition CurrentTrack { get { return GameContent.GetTrack(Session.trackId); } }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // A second director means two scenes both carry one - usually because a track scene
                // was opened directly. The existing one owns the session, so this copy goes.
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // State first, scene management second. DontDestroyOnLoad throws outside play mode,
            // and anything after it in this method would then never run - which left the director
            // alive with a null Session and every later call failing somewhere far away from the
            // real cause. Built this way round, the director is always usable once Awake is
            // entered, and surviving scene loads is the part allowed to fail.
            Session = new GameSession();
            Role = NetworkRole.Offline;
            Phase = GamePhase.Boot;

            GameContent.Bind(modeCatalog, trackCatalog);
            WarnAboutContentProblems();

            transform.SetParent(null);
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            // Boot is left until Start rather than Awake so that anything listening for
            // PhaseChanged has had its own Awake to subscribe in and does not miss the first
            // transition.
            if (Phase == GamePhase.Boot)
                EnterPhase(startPhase == GamePhase.Boot ? GamePhase.MainMenu : startPhase);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ------------------------------------------------------------------ phase

        /// <summary>
        /// Which phases may follow which. Written out rather than inferred because the illegal
        /// moves are the interesting ones: you cannot get to Results without racing, and you cannot
        /// reach a track without going through Loading, no matter what a message claims.
        ///
        /// MainMenu is reachable from everywhere - that is quitting out, and it must always work.
        /// </summary>
        public static bool CanTransition(GamePhase from, GamePhase to)
        {
            if (from == to)
                return false;
            if (to == GamePhase.Boot)
                return false;
            if (to == GamePhase.MainMenu)
                return true;

            switch (from)
            {
                case GamePhase.Boot:
                    return to == GamePhase.Lobby;
                case GamePhase.MainMenu:
                    return to == GamePhase.Lobby;
                case GamePhase.Lobby:
                    return to == GamePhase.TrackSelect || to == GamePhase.Loading;
                case GamePhase.TrackSelect:
                    return to == GamePhase.Lobby || to == GamePhase.Loading;
                case GamePhase.Loading:
                    return to == GamePhase.Race;
                case GamePhase.Race:
                    return to == GamePhase.Results;
                case GamePhase.Results:
                    return to == GamePhase.Lobby || to == GamePhase.TrackSelect || to == GamePhase.Loading;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Ask to move to a phase. Returns false and raises <see cref="RequestRejected"/> if the
        /// move is not legal from where the game is.
        /// </summary>
        public bool RequestPhase(GamePhase next)
        {
            if (!CanTransition(Phase, next))
                return Reject(string.Format("Cannot go from {0} to {1}.", Phase, next));

            EnterPhase(next);
            return true;
        }

        void EnterPhase(GamePhase next)
        {
            GamePhase previous = Phase;
            Phase = next;

            if (next == GamePhase.MainMenu)
                OnEnteredMainMenu();

            PhaseChanged?.Invoke(previous, next);
        }

        void OnEnteredMainMenu()
        {
            if (_loadRoutine != null)
            {
                StopCoroutine(_loadRoutine);
                _loadRoutine = null;
            }

            Session.Clear();
            Role = NetworkRole.Offline;
            LoadProgress = 0f;
            RaiseSessionChanged();
        }

        // ------------------------------------------------------------------ session lifecycle

        /// <summary>
        /// Open a single-machine session and go to the lobby. The offline path and the eventual
        /// host path deliberately share everything below the role assignment, so that "play alone"
        /// and "host a game" cannot drift into two different lobbies.
        /// </summary>
        public bool RequestStartOfflineSession()
        {
            return StartSession(NetworkRole.Offline);
        }

        /// <summary>
        /// Open a session this machine owns. Identical to the offline path today - the transport
        /// does not exist - but separated now so callers are already written against the right one.
        /// </summary>
        public bool RequestHostSession()
        {
            return StartSession(NetworkRole.Host);
        }

        bool StartSession(NetworkRole role)
        {
            if (Phase != GamePhase.MainMenu && Phase != GamePhase.Boot)
                return Reject("A session is already open.");

            Role = role;
            Session.Clear();
            Session.sessionId = Guid.NewGuid().ToString("N");
            Session.raceSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            GameModeDefinition mode = modeCatalog != null ? modeCatalog.Default() : null;
            if (mode != null)
            {
                Session.modeId = mode.modeId;
                Session.maxRacers = mode.maxRacers;
                Session.aiFillTarget = mode.maxRacers;
                Session.aiFillEnabled = mode.allowsAiFill;
            }

            AddRacerInternal(RacerKind.LocalPlayer, localPlayerName, 0);

            if (!RequestPhase(GamePhase.Lobby))
            {
                // Boot -> Lobby and MainMenu -> Lobby are both legal, so this should not happen.
                // If it ever does the session is half-built, and the title screen is the safe place.
                EnterPhase(GamePhase.MainMenu);
                return false;
            }

            RaiseSessionChanged();
            return true;
        }

        /// <summary>Leave whatever is happening and go back to the title screen.</summary>
        public bool RequestLeaveSession()
        {
            if (Phase == GamePhase.MainMenu)
                return false;

            EnterPhase(GamePhase.MainMenu);
            LoadFrontEndScene();
            return true;
        }

        // ------------------------------------------------------------------ lobby choices

        public bool RequestSelectMode(string modeId)
        {
            if (!HasAuthority)
                return Reject("Only the host can change the mode.");
            if (Phase != GamePhase.Lobby)
                return Reject("The mode can only be changed in the lobby.");

            GameModeDefinition mode = GameContent.GetMode(modeId);
            if (mode == null || !mode.IsValid)
                return Reject(string.Format("No mode with id '{0}'.", modeId));

            Session.modeId = mode.modeId;
            Session.maxRacers = mode.maxRacers;
            Session.aiFillEnabled = mode.allowsAiFill && Session.aiFillEnabled;
            Session.aiFillTarget = Mathf.Clamp(Session.aiFillTarget, mode.minRacers, mode.maxRacers);

            // A mode change can invalidate the chosen track, since a mode may cap the grid below
            // what that track needs, or not use track select at all.
            TrackDefinition track = CurrentTrack;
            if (track != null && !track.IsSelectable)
                Session.trackId = string.Empty;

            TrimGridToLimit();
            RaiseSessionChanged();
            return true;
        }

        public bool RequestSelectTrack(string trackId)
        {
            if (!HasAuthority)
                return Reject("Only the host can choose the track.");
            if (Phase != GamePhase.TrackSelect && Phase != GamePhase.Lobby)
                return Reject("The track can only be chosen from the lobby or the map picker.");

            TrackDefinition track = GameContent.GetTrack(trackId);
            if (track == null)
                return Reject(string.Format("No track with id '{0}'.", trackId));
            if (!track.IsSelectable)
                return Reject(string.Format("'{0}' is not playable yet.", track.displayName));

            Session.trackId = track.trackId;
            Session.maxRacers = Mathf.Min(Session.maxRacers, track.maxRacers);
            TrimGridToLimit();
            RaiseSessionChanged();
            return true;
        }

        public bool RequestSetAiFill(bool enabled, int target)
        {
            if (!HasAuthority)
                return Reject("Only the host can change AI fill.");

            GameModeDefinition mode = CurrentMode;
            if (enabled && mode != null && !mode.allowsAiFill)
                return Reject(string.Format("{0} does not use AI racers.", mode.displayName));

            Session.aiFillEnabled = enabled;
            Session.aiFillTarget = mode != null
                ? Mathf.Clamp(target, mode.minRacers, Mathf.Min(mode.maxRacers, Session.maxRacers))
                : Mathf.Clamp(target, 1, Session.maxRacers);

            RaiseSessionChanged();
            return true;
        }

        public bool RequestSetLapCount(int laps)
        {
            if (!HasAuthority)
                return Reject("Only the host can change the lap count.");

            Session.lapCountOverride = Mathf.Max(0, laps);
            RaiseSessionChanged();
            return true;
        }

        public bool RequestSetReady(string racerId, bool ready)
        {
            RacerSlot slot = Session.FindRacer(racerId);
            if (slot == null)
                return Reject("That racer is not in the session.");
            if (!HasAuthority && !slot.IsLocal)
                return Reject("You can only ready up your own racer.");

            slot.isReady = ready;
            RaiseSessionChanged();
            return true;
        }

        // ------------------------------------------------------------------ roster

        /// <summary>Add a second person on this machine - the split-screen path.</summary>
        public bool RequestAddLocalPlayer(string displayName)
        {
            if (!HasAuthority)
                return Reject("Only the host can add players.");
            if (!Session.HasRoom)
                return Reject("The grid is full.");

            AddRacerInternal(RacerKind.LocalPlayer, displayName, Session.LocalCount);
            RaiseSessionChanged();
            return true;
        }

        /// <summary>
        /// Add one AI racer. Public because the lobby will want to add them by hand as well as by
        /// fill, and because it is the seam the AI work plugs into: when bots exist, only what
        /// reads these slots changes, not what makes them.
        /// </summary>
        public bool RequestAddAiRacer(float skill)
        {
            if (!HasAuthority)
                return Reject("Only the host can add AI racers.");
            if (!Session.HasRoom)
                return Reject("The grid is full.");

            GameModeDefinition mode = CurrentMode;
            if (mode != null && !mode.allowsAiFill)
                return Reject(string.Format("{0} does not use AI racers.", mode.displayName));

            RacerSlot slot = AddRacerInternal(RacerKind.Ai, NextAiName(), 0);
            slot.aiSkill = Mathf.Clamp01(skill);
            RaiseSessionChanged();
            return true;
        }

        public bool RequestRemoveRacer(string racerId)
        {
            if (!HasAuthority)
                return Reject("Only the host can remove racers.");

            for (int i = 0; i < Session.racers.Count; i++)
            {
                if (Session.racers[i].racerId != racerId)
                    continue;

                Session.racers.RemoveAt(i);
                RaiseSessionChanged();
                return true;
            }

            return Reject("That racer is not in the session.");
        }

        RacerSlot AddRacerInternal(RacerKind kind, string displayName, int localIndex)
        {
            var slot = new RacerSlot
            {
                racerId = Guid.NewGuid().ToString("N"),
                displayName = string.IsNullOrEmpty(displayName) ? "Racer" : displayName,
                kind = kind,
                ownerClientId = 0,
                localPlayerIndex = localIndex,
            };

            Session.racers.Add(slot);
            return slot;
        }

        string NextAiName()
        {
            int aiIndex = Session.AiCount;
            if (aiRacerNames != null && aiIndex < aiRacerNames.Length)
                return aiRacerNames[aiIndex];
            return string.Format("CPU {0}", aiIndex + 1);
        }

        /// <summary>
        /// Top the grid up to <see cref="GameSession.aiFillTarget"/> with bots. Called on the way
        /// out of the lobby, so the grid the race receives is already complete and nothing
        /// downstream has to care that some of it is not human.
        /// </summary>
        public int FillGridWithAi()
        {
            if (!HasAuthority || !Session.aiFillEnabled)
                return 0;

            GameModeDefinition mode = CurrentMode;
            if (mode != null && !mode.allowsAiFill)
                return 0;

            int target = Mathf.Min(Session.aiFillTarget, Session.maxRacers);
            int added = 0;
            while (Session.OccupiedCount < target)
            {
                RacerSlot slot = AddRacerInternal(RacerKind.Ai, NextAiName(), 0);
                slot.aiSkill = Mathf.Clamp01(0.35f + 0.05f * added);
                added++;
            }

            if (added > 0)
                RaiseSessionChanged();
            return added;
        }

        /// <summary>Drop racers that no longer fit after a mode or track narrowed the grid, bots first.</summary>
        void TrimGridToLimit()
        {
            for (int i = Session.racers.Count - 1; i >= 0 && Session.OccupiedCount > Session.maxRacers; i--)
            {
                if (Session.racers[i].kind == RacerKind.Ai)
                    Session.racers.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ leaving the lobby

        /// <summary>
        /// The lobby's continue button. Routes rather than hard-coding a destination, because
        /// whether a track gets picked is the mode's business - a Grand Prix runs a fixed cup and
        /// goes straight to loading.
        /// </summary>
        public bool RequestAdvanceFromLobby()
        {
            if (Phase != GamePhase.Lobby)
                return Reject("Not in the lobby.");
            if (!HasAuthority)
                return Reject("Waiting for the host.");
            if (!Session.EveryoneReady)
                return Reject("Not everyone is ready.");

            GameModeDefinition mode = CurrentMode;
            if (mode == null)
                return Reject("No mode chosen.");

            if (mode.usesTrackSelect)
                return RequestPhase(GamePhase.TrackSelect);

            return RequestStartRace();
        }

        /// <summary>
        /// Settle the grid and load the track. This is the hand-off: everything past the loaded
        /// scene is RaceSession's problem.
        /// </summary>
        public bool RequestStartRace()
        {
            if (!HasAuthority)
                return Reject("Only the host can start the race.");
            if (Phase != GamePhase.Lobby && Phase != GamePhase.TrackSelect && Phase != GamePhase.Results)
                return Reject("The race can only be started from the lobby, the map picker or the results.");

            GameModeDefinition mode = CurrentMode;
            if (mode == null)
                return Reject("No mode chosen.");

            TrackDefinition track = CurrentTrack;
            if (track == null && trackCatalog != null)
            {
                // A mode that skips track select still needs somewhere to race.
                track = trackCatalog.FirstSelectable();
                if (track != null)
                    Session.trackId = track.trackId;
            }

            if (track == null)
                return Reject("No track chosen.");
            if (!track.IsSelectable)
                return Reject(string.Format("'{0}' is not playable yet.", track.displayName));

            FillGridWithAi();

            if (Session.OccupiedCount < mode.minRacers)
                return Reject(string.Format("{0} needs at least {1} racers.", mode.displayName, mode.minRacers));

            // Rerolled per race rather than per session so a rematch is not a replay, but still
            // decided once, here, by the machine with authority.
            Session.raceSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            RaiseSessionChanged();

            if (!RequestPhase(GamePhase.Loading))
                return false;

            _loadRoutine = StartCoroutine(LoadTrackRoutine(track));
            return true;
        }

        IEnumerator LoadTrackRoutine(TrackDefinition track)
        {
            LoadProgress = 0f;

            string sceneName = track.SceneName;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                // The single most common way this fails, and Unity's own error for it does not say
                // what to do about it, so say what is actually wrong and where it gets fixed.
                Debug.LogErrorFormat(this,
                    "Track '{0}' points at scene '{1}', which is not in Build Settings. " +
                    "Add it under File > Build Profiles > Scene List.",
                    track.displayName, track.scenePath);
                Reject(string.Format("'{0}' could not be loaded.", track.displayName));
                _loadRoutine = null;
                EnterPhase(GamePhase.MainMenu);
                yield break;
            }

            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            while (load != null && !load.isDone)
            {
                LoadProgress = load.progress;
                yield return null;
            }

            LoadProgress = 1f;
            _loadRoutine = null;
            EnterPhase(GamePhase.Race);
        }

        /// <summary>Called by the race when the flag drops. RaceSession will own the standings.</summary>
        public bool RequestFinishRace()
        {
            return RequestPhase(GamePhase.Results);
        }

        /// <summary>Results screen's "back to lobby". Clears ready flags so nobody starts by accident.</summary>
        public bool RequestReturnToLobby()
        {
            if (Phase != GamePhase.Results)
                return Reject("Not on the results screen.");

            for (int i = 0; i < Session.racers.Count; i++)
                Session.racers[i].isReady = false;

            if (!RequestPhase(GamePhase.Lobby))
                return false;

            LoadFrontEndScene();
            RaiseSessionChanged();
            return true;
        }

        void LoadFrontEndScene()
        {
            if (string.IsNullOrEmpty(frontEndScenePath))
                return;

            string sceneName = SceneNameFromPath(frontEndScenePath);
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogErrorFormat(this,
                    "Front end scene '{0}' is not in Build Settings.", frontEndScenePath);
                return;
            }

            if (SceneManager.GetActiveScene().name != sceneName)
                SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        static string SceneNameFromPath(string path)
        {
            int slash = path.LastIndexOf('/');
            int start = slash >= 0 ? slash + 1 : 0;
            int dot = path.LastIndexOf('.');
            int end = dot > start ? dot : path.Length;
            return path.Substring(start, end - start);
        }

        // ------------------------------------------------------------------ plumbing

        void RaiseSessionChanged()
        {
            SessionChanged?.Invoke();
        }

        bool Reject(string reason)
        {
            RequestRejected?.Invoke(reason);
            return false;
        }

        /// <summary>
        /// Duplicate ids are checked once at startup rather than left to be discovered. Two tracks
        /// answering to one id is the kind of fault where the host and a client each resolve it
        /// locally, get different assets, and both believe they agree.
        /// </summary>
        void WarnAboutContentProblems()
        {
            if (modeCatalog == null)
                Debug.LogWarning("GameDirector has no mode catalog - the lobby will have nothing to offer.", this);
            if (trackCatalog == null)
                Debug.LogWarning("GameDirector has no track catalog - the map picker will be empty.", this);

            if (trackCatalog != null)
            {
                List<string> duplicates = trackCatalog.FindDuplicateIds();
                for (int i = 0; i < duplicates.Count; i++)
                    Debug.LogErrorFormat(this, "Two tracks share the id '{0}'.", duplicates[i]);
            }

            if (modeCatalog != null)
            {
                List<string> duplicates = modeCatalog.FindDuplicateIds();
                for (int i = 0; i < duplicates.Count; i++)
                    Debug.LogErrorFormat(this, "Two modes share the id '{0}'.", duplicates[i]);
            }
        }
    }
}
