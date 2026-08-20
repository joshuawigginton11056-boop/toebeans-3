using NUnit.Framework;
using UnityEngine;
using Toebeans.Game;

namespace Toebeans.Game.Tests
{
    /// <summary>
    /// Session state is the thing that will be replicated, so its queries have to mean the same
    /// on every machine. The two that carry real rules are the ready check and lap resolution.
    /// </summary>
    public class GameSessionTests
    {
        static RacerSlot Racer(RacerKind kind, bool ready = false)
        {
            return new RacerSlot
            {
                racerId = System.Guid.NewGuid().ToString("N"),
                displayName = kind.ToString(),
                kind = kind,
                isReady = ready,
            };
        }

        [Test]
        public void BotsDoNotHoldUpTheCountdown()
        {
            // The failure this guards: bots cannot press ready, so counting them as unready would
            // make any grid with a bot on it impossible to start.
            var session = new GameSession();
            session.racers.Add(Racer(RacerKind.LocalPlayer, ready: true));
            session.racers.Add(Racer(RacerKind.Ai));
            session.racers.Add(Racer(RacerKind.Ai));

            Assert.IsTrue(session.EveryoneReady);
        }

        [Test]
        public void OneUnreadyPersonBlocksTheStart()
        {
            var session = new GameSession();
            session.racers.Add(Racer(RacerKind.LocalPlayer, ready: true));
            session.racers.Add(Racer(RacerKind.RemotePlayer, ready: false));

            Assert.IsFalse(session.EveryoneReady);
        }

        [Test]
        public void AGridOfNothingButBotsIsStartable()
        {
            var session = new GameSession();
            session.racers.Add(Racer(RacerKind.Ai));

            Assert.IsTrue(session.EveryoneReady);
        }

        [Test]
        public void CountsIgnoreEmptySeats()
        {
            var session = new GameSession();
            session.racers.Add(Racer(RacerKind.LocalPlayer));
            session.racers.Add(Racer(RacerKind.Ai));
            session.racers.Add(Racer(RacerKind.Empty));

            Assert.AreEqual(2, session.OccupiedCount);
            Assert.AreEqual(1, session.LocalCount);
            Assert.AreEqual(1, session.AiCount);
        }

        [Test]
        public void LapCountPrefersOverrideThenTrackThenMode()
        {
            var mode = ScriptableObject.CreateInstance<GameModeDefinition>();
            mode.defaultLapCount = 3;
            var track = ScriptableObject.CreateInstance<TrackDefinition>();
            track.defaultLapCount = 5;

            var session = new GameSession();

            // The track wins over the mode, because lap count is really a property of lap length.
            Assert.AreEqual(5, session.ResolveLapCount(mode, track));

            track.defaultLapCount = 0;
            Assert.AreEqual(3, session.ResolveLapCount(mode, track));

            session.lapCountOverride = 7;
            Assert.AreEqual(7, session.ResolveLapCount(mode, track));

            Object.DestroyImmediate(mode);
            Object.DestroyImmediate(track);
        }

        [Test]
        public void ClearWipesTheRosterAndTheChoices()
        {
            var session = new GameSession();
            session.sessionId = "abc";
            session.modeId = "vs_race";
            session.trackId = "lava_world";
            session.racers.Add(Racer(RacerKind.LocalPlayer));

            session.Clear();

            Assert.IsEmpty(session.sessionId);
            Assert.IsEmpty(session.modeId);
            Assert.IsEmpty(session.trackId);
            Assert.AreEqual(0, session.racers.Count);
        }

        [Test]
        public void TrackSceneNameStripsPathAndExtension()
        {
            var track = ScriptableObject.CreateInstance<TrackDefinition>();
            track.scenePath = "Assets/Scenes/LavaWorld.unity";

            Assert.AreEqual("LavaWorld", track.SceneName);

            Object.DestroyImmediate(track);
        }
    }
}
