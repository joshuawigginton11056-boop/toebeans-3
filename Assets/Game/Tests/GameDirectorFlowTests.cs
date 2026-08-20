using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Toebeans.Game;

namespace Toebeans.Game.Tests
{
    /// <summary>
    /// Walks a real <see cref="GameDirector"/> through the flow the UI will drive: title, lobby,
    /// mode, track, grid. Play mode rather than edit mode because the director is a MonoBehaviour
    /// whose first phase is entered in Start.
    ///
    /// Nothing here loads a scene. Starting a race is verified up to the point where the track is
    /// settled and the grid is complete - the load itself belongs to a test with real scenes in
    /// Build Settings, and mixing the two would make this suite depend on project settings.
    /// </summary>
    public class GameDirectorFlowTests
    {
        GameObject _host;
        GameDirector _director;
        GameModeCatalog _modes;
        TrackCatalog _tracks;

        [SetUp]
        public void SetUp()
        {
            _modes = ScriptableObject.CreateInstance<GameModeCatalog>();
            _modes.modes.Add(Mode("vs_race", "VS Race", maxRacers: 8, aiFill: true, trackSelect: true));
            _modes.modes.Add(Mode("grand_prix", "Grand Prix", maxRacers: 8, aiFill: true, trackSelect: false));
            _modes.modes.Add(Mode("time_trial", "Time Trial", maxRacers: 1, aiFill: false, trackSelect: true));
            _modes.defaultModeId = "vs_race";

            _tracks = ScriptableObject.CreateInstance<TrackCatalog>();
            _tracks.tracks.Add(Track("test_track", "Test Track", playable: true));
            _tracks.tracks.Add(Track("unfinished", "Unfinished", playable: false));

            // Built inactive so the catalogs are in place before Awake binds them.
            _host = new GameObject("TestGameDirector");
            _host.SetActive(false);
            _director = _host.AddComponent<GameDirector>();
            _director.modeCatalog = _modes;
            _director.trackCatalog = _tracks;
            _director.startPhase = GamePhase.MainMenu;
            _director.frontEndScenePath = string.Empty;
            _host.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
                Object.DestroyImmediate(_host);
            if (_modes != null)
                Object.DestroyImmediate(_modes);
            if (_tracks != null)
                Object.DestroyImmediate(_tracks);
        }

        static GameModeDefinition Mode(string id, string name, int maxRacers, bool aiFill, bool trackSelect)
        {
            var mode = ScriptableObject.CreateInstance<GameModeDefinition>();
            mode.modeId = id;
            mode.displayName = name;
            mode.minRacers = 1;
            mode.maxRacers = maxRacers;
            mode.allowsAiFill = aiFill;
            mode.usesTrackSelect = trackSelect;
            mode.defaultLapCount = 3;
            return mode;
        }

        static TrackDefinition Track(string id, string name, bool playable)
        {
            var track = ScriptableObject.CreateInstance<TrackDefinition>();
            track.trackId = id;
            track.displayName = name;
            track.scenePath = "Assets/Scenes/DoesNotMatter.unity";
            track.maxRacers = 8;
            track.isPlayable = playable;
            return track;
        }

        [UnityTest]
        public IEnumerator OpensOnTheTitleScreen()
        {
            yield return null;

            Assert.AreEqual(GamePhase.MainMenu, _director.Phase);
            Assert.AreEqual(0, _director.Session.OccupiedCount, "The title screen should have no roster.");
        }

        [UnityTest]
        public IEnumerator StartingASessionPutsOnePlayerInTheLobby()
        {
            yield return null;

            Assert.IsTrue(_director.RequestStartOfflineSession());
            Assert.AreEqual(GamePhase.Lobby, _director.Phase);
            Assert.AreEqual(1, _director.Session.OccupiedCount);
            Assert.AreEqual(1, _director.Session.LocalCount);
            Assert.AreEqual("vs_race", _director.Session.modeId, "Should open on the catalog's default mode.");
            Assert.IsNotEmpty(_director.Session.sessionId);
        }

        [UnityTest]
        public IEnumerator TheLobbyWillNotAdvanceUntilEveryoneIsReady()
        {
            yield return null;
            _director.RequestStartOfflineSession();

            Assert.IsFalse(_director.RequestAdvanceFromLobby(), "An unready player should block the start.");
            Assert.AreEqual(GamePhase.Lobby, _director.Phase);

            RacerSlot player = _director.Session.PrimaryLocalRacer;
            Assert.IsTrue(_director.RequestSetReady(player.racerId, true));
            Assert.IsTrue(_director.RequestAdvanceFromLobby());
            Assert.AreEqual(GamePhase.TrackSelect, _director.Phase);
        }

        [UnityTest]
        public IEnumerator AModeWithoutTrackSelectSkipsStraightPastIt()
        {
            yield return null;
            _director.RequestStartOfflineSession();
            _director.RequestSelectMode("grand_prix");
            _director.RequestSetReady(_director.Session.PrimaryLocalRacer.racerId, true);

            var phases = new System.Collections.Generic.List<GamePhase>();
            _director.PhaseChanged += (from, to) => phases.Add(to);

            // These fixture tracks point at a scene that does not exist, so the load bails out and
            // says so. That is the behaviour being relied on here - it is what lets the phase
            // routing be tested without putting a real scene in Build Settings.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("not in Build Settings"));

            Assert.IsTrue(_director.RequestAdvanceFromLobby());

            Assert.Contains(GamePhase.Loading, phases,
                "Grand Prix picks its own tracks, so it should go straight to loading without track select.");
            Assert.IsFalse(phases.Contains(GamePhase.TrackSelect),
                "Grand Prix should never show the map picker.");
            Assert.AreEqual("test_track", _director.Session.trackId,
                "It should have fallen back to the first selectable track.");
        }

        [UnityTest]
        public IEnumerator AMissingTrackSceneFailsToTheTitleRatherThanHanging()
        {
            yield return null;
            _director.RequestStartOfflineSession();
            _director.RequestSelectTrack("test_track");

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("not in Build Settings"));

            _director.RequestStartRace();

            // The worst outcome would be sitting on a loading screen forever, so the failure path
            // is asserted as deliberately as the success one.
            Assert.AreEqual(GamePhase.MainMenu, _director.Phase);
        }

        [UnityTest]
        public IEnumerator UnfinishedTracksAreRefused()
        {
            yield return null;
            _director.RequestStartOfflineSession();

            Assert.IsFalse(_director.RequestSelectTrack("unfinished"));
            Assert.IsEmpty(_director.Session.trackId);

            Assert.IsTrue(_director.RequestSelectTrack("test_track"));
            Assert.AreEqual("test_track", _director.Session.trackId);
        }

        [UnityTest]
        public IEnumerator UnknownIdsAreRefusedRatherThanApplied()
        {
            yield return null;
            _director.RequestStartOfflineSession();

            Assert.IsFalse(_director.RequestSelectTrack("no_such_track"));
            Assert.IsFalse(_director.RequestSelectMode("no_such_mode"));
            Assert.AreEqual("vs_race", _director.Session.modeId, "A bad id must not clear the good one.");
        }

        [UnityTest]
        public IEnumerator AiFillTopsUpAnEmptyGrid()
        {
            yield return null;
            _director.RequestStartOfflineSession();

            int added = _director.FillGridWithAi();

            Assert.AreEqual(7, added);
            Assert.AreEqual(8, _director.Session.OccupiedCount);
            Assert.AreEqual(7, _director.Session.AiCount);
            Assert.AreEqual(1, _director.Session.LocalCount);
        }

        [UnityTest]
        public IEnumerator AiFillRespectsTheTarget()
        {
            yield return null;
            _director.RequestStartOfflineSession();
            _director.RequestSetAiFill(true, 4);

            _director.FillGridWithAi();

            Assert.AreEqual(4, _director.Session.OccupiedCount);
            Assert.AreEqual(3, _director.Session.AiCount);
        }

        [UnityTest]
        public IEnumerator ModesThatRefuseBotsGetNone()
        {
            yield return null;
            _director.RequestStartOfflineSession();
            _director.RequestSelectMode("time_trial");

            Assert.IsFalse(_director.RequestAddAiRacer(0.5f), "Time Trial has nobody to race.");
            Assert.AreEqual(0, _director.FillGridWithAi());
            Assert.AreEqual(1, _director.Session.OccupiedCount);
        }

        [UnityTest]
        public IEnumerator NarrowingTheModeTrimsBotsOffTheGrid()
        {
            yield return null;
            _director.RequestStartOfflineSession();
            _director.FillGridWithAi();
            Assert.AreEqual(8, _director.Session.OccupiedCount);

            // Time Trial seats one. The person must survive and the bots must not.
            _director.RequestSelectMode("time_trial");

            Assert.AreEqual(1, _director.Session.OccupiedCount);
            Assert.AreEqual(1, _director.Session.LocalCount);
            Assert.AreEqual(0, _director.Session.AiCount);
        }

        [UnityTest]
        public IEnumerator AClientMayNotChangeSessionState()
        {
            yield return null;
            _director.RequestStartOfflineSession();
            string modeBefore = _director.Session.modeId;

            _director.SetRole(NetworkRole.Client);

            Assert.IsFalse(_director.HasAuthority);
            Assert.IsFalse(_director.RequestSelectMode("grand_prix"));
            Assert.IsFalse(_director.RequestSelectTrack("test_track"));
            Assert.IsFalse(_director.RequestAddAiRacer(0.5f));
            Assert.IsFalse(_director.RequestStartRace());
            Assert.AreEqual(modeBefore, _director.Session.modeId, "A refused request must not half-apply.");
        }

        [UnityTest]
        public IEnumerator AClientMayStillReadyItsOwnRacer()
        {
            yield return null;
            _director.RequestStartOfflineSession();
            RacerSlot player = _director.Session.PrimaryLocalRacer;

            _director.SetRole(NetworkRole.Client);

            Assert.IsTrue(_director.RequestSetReady(player.racerId, true),
                "Readying up is the one thing a client does to its own seat.");
            Assert.IsTrue(player.isReady);
        }

        [UnityTest]
        public IEnumerator LeavingASessionClearsIt()
        {
            yield return null;
            _director.RequestStartOfflineSession();
            _director.FillGridWithAi();

            Assert.IsTrue(_director.RequestLeaveSession());

            Assert.AreEqual(GamePhase.MainMenu, _director.Phase);
            Assert.AreEqual(0, _director.Session.OccupiedCount);
            Assert.IsEmpty(_director.Session.modeId);
        }

        [UnityTest]
        public IEnumerator PhaseChangesAreAnnouncedOnce()
        {
            yield return null;

            int calls = 0;
            GamePhase seenFrom = GamePhase.Boot;
            GamePhase seenTo = GamePhase.Boot;
            _director.PhaseChanged += (from, to) =>
            {
                calls++;
                seenFrom = from;
                seenTo = to;
            };

            _director.RequestStartOfflineSession();

            Assert.AreEqual(1, calls);
            Assert.AreEqual(GamePhase.MainMenu, seenFrom);
            Assert.AreEqual(GamePhase.Lobby, seenTo);
        }

        [UnityTest]
        public IEnumerator RefusedRequestsExplainThemselves()
        {
            yield return null;

            string reason = null;
            _director.RequestRejected += r => reason = r;

            _director.RequestSelectTrack("no_such_track");

            Assert.IsNotNull(reason, "A refusal the UI cannot explain is a dead button.");
            Assert.IsNotEmpty(reason);
        }
    }
}
