using NUnit.Framework;
using Toebeans.Game;

namespace Toebeans.Game.Tests
{
    /// <summary>
    /// The phase table is the one piece of this system a network message will be allowed to drive,
    /// so what it REFUSES matters more than what it allows. These tests are mostly about the
    /// refusals: a client that receives a stale or forged phase must not end up somewhere the
    /// player could not have walked to.
    /// </summary>
    public class GamePhaseTransitionTests
    {
        [Test]
        public void TitleToLobbyIsAllowed()
        {
            Assert.IsTrue(GameDirector.CanTransition(GamePhase.MainMenu, GamePhase.Lobby));
        }

        [Test]
        public void LobbyReachesTrackSelectAndLoading()
        {
            Assert.IsTrue(GameDirector.CanTransition(GamePhase.Lobby, GamePhase.TrackSelect));
            Assert.IsTrue(GameDirector.CanTransition(GamePhase.Lobby, GamePhase.Loading));
        }

        [Test]
        public void TrackSelectCanGoBackToLobby()
        {
            Assert.IsTrue(GameDirector.CanTransition(GamePhase.TrackSelect, GamePhase.Lobby));
        }

        [Test]
        public void QuittingToTheTitleAlwaysWorks()
        {
            // Whatever has gone wrong, the way out has to exist.
            foreach (GamePhase phase in System.Enum.GetValues(typeof(GamePhase)))
            {
                if (phase == GamePhase.MainMenu)
                    continue;
                Assert.IsTrue(GameDirector.CanTransition(phase, GamePhase.MainMenu),
                    "Could not quit to the title from " + phase);
            }
        }

        [Test]
        public void RaceCannotBeEnteredWithoutLoading()
        {
            Assert.IsFalse(GameDirector.CanTransition(GamePhase.Lobby, GamePhase.Race));
            Assert.IsFalse(GameDirector.CanTransition(GamePhase.TrackSelect, GamePhase.Race));
            Assert.IsFalse(GameDirector.CanTransition(GamePhase.MainMenu, GamePhase.Race));
            Assert.IsTrue(GameDirector.CanTransition(GamePhase.Loading, GamePhase.Race));
        }

        [Test]
        public void ResultsCannotBeReachedWithoutRacing()
        {
            Assert.IsFalse(GameDirector.CanTransition(GamePhase.MainMenu, GamePhase.Results));
            Assert.IsFalse(GameDirector.CanTransition(GamePhase.Lobby, GamePhase.Results));
            Assert.IsFalse(GameDirector.CanTransition(GamePhase.Loading, GamePhase.Results));
            Assert.IsTrue(GameDirector.CanTransition(GamePhase.Race, GamePhase.Results));
        }

        [Test]
        public void NothingReturnsToBoot()
        {
            foreach (GamePhase phase in System.Enum.GetValues(typeof(GamePhase)))
                Assert.IsFalse(GameDirector.CanTransition(phase, GamePhase.Boot));
        }

        [Test]
        public void APhaseCannotTransitionToItself()
        {
            foreach (GamePhase phase in System.Enum.GetValues(typeof(GamePhase)))
                Assert.IsFalse(GameDirector.CanTransition(phase, phase));
        }

        [Test]
        public void PhaseValuesAreStable()
        {
            // These numbers go over the wire. Renumbering them silently moves players between
            // screens across a version mismatch, so they are pinned here deliberately.
            Assert.AreEqual(0, (int)GamePhase.Boot);
            Assert.AreEqual(1, (int)GamePhase.MainMenu);
            Assert.AreEqual(2, (int)GamePhase.Lobby);
            Assert.AreEqual(3, (int)GamePhase.TrackSelect);
            Assert.AreEqual(4, (int)GamePhase.Loading);
            Assert.AreEqual(5, (int)GamePhase.Race);
            Assert.AreEqual(6, (int)GamePhase.Results);
        }
    }
}
