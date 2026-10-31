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

        [Theory]
        [InlineData(0, RemuteDecision.Remute)]
        [InlineData(1, RemuteDecision.Remute)]
        [InlineData(2, RemuteDecision.Remute)]
        [InlineData(3, RemuteDecision.GiveUp)]
        [InlineData(4, RemuteDecision.GiveUp)]
        public void InsideWindow_GivesUpOnceTheBudgetIsUsedUp(int remutesDone, RemuteDecision expected)
        {
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
