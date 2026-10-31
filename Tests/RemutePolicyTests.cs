using System;
using Xunit;

namespace SteelSeriesDiscordStreamFix.Tests
{
    public class RemutePolicyTests
    {
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(15);

        [Fact]
        public void MutedSession_IsIgnored()
        {
            Assert.Equal(RemuteDecision.Ignore,
                RemutePolicy.Decide(true, TimeSpan.FromSeconds(1), Window, 0, 3));
        }

        [Fact]
        public void MutedSession_IsIgnored_EvenAfterTheWindow()
        {
            Assert.Equal(RemuteDecision.Ignore,
                RemutePolicy.Decide(true, TimeSpan.FromMinutes(5), Window, 99, 3));
        }

        [Fact]
        public void UnmutedInsideWindow_IsRemuted()
        {
            Assert.Equal(RemuteDecision.Remute,
                RemutePolicy.Decide(false, TimeSpan.FromSeconds(2), Window, 0, 3));
        }

        [Fact]
        public void UnmutedExactlyAtWindowEnd_IsStillInsideTheWindow()
        {
            Assert.Equal(RemuteDecision.Remute,
                RemutePolicy.Decide(false, Window, Window, 0, 3));
        }

        [Fact]
        public void UnmutedJustAfterWindow_IsADeliberateUnmute()
        {
            Assert.Equal(RemuteDecision.RespectUserUnmute,
                RemutePolicy.Decide(false, Window + TimeSpan.FromMilliseconds(1), Window, 0, 3));
        }

        [Fact]
        public void AfterTheWindow_TheUserWins_EvenWhenRemuteBudgetIsUsedUp()
        {
            Assert.Equal(RemuteDecision.RespectUserUnmute,
                RemutePolicy.Decide(false, TimeSpan.FromMinutes(1), Window, 3, 3));
        }

        // The enum is internal, so a public test method cannot take it as a parameter: use a bool instead.
        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(3, true)]
        [InlineData(4, true)]
        public void InsideWindow_GivesUpOnceTheBudgetIsUsedUp(int remutesDone, bool expectGiveUp)
        {
            RemuteDecision expected = expectGiveUp ? RemuteDecision.GiveUp : RemuteDecision.Remute;
            Assert.Equal(expected,
                RemutePolicy.Decide(false, TimeSpan.FromSeconds(5), Window, remutesDone, 3));
        }

        [Fact]
        public void LongerWindowAndBiggerBudget_AreHonoured()
        {
            var window = TimeSpan.FromSeconds(60);
            Assert.Equal(RemuteDecision.Remute, RemutePolicy.Decide(false, TimeSpan.FromSeconds(45), window, 5, 10));
            Assert.Equal(RemuteDecision.GiveUp, RemutePolicy.Decide(false, TimeSpan.FromSeconds(45), window, 10, 10));
        }
    }
}
