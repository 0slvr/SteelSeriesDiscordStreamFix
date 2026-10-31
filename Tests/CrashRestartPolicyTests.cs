using System;
using Xunit;

namespace SteelSeriesDiscordStreamFix.Tests
{
    public class CrashRestartPolicyTests
    {
        private static readonly TimeSpan Stable = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan Quick = TimeSpan.FromSeconds(20);

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        [InlineData(2, 3)]
        public void QuickCrashes_CountUp(int current, int expectedNext)
        {
            Assert.Equal(expectedNext, CrashRestartPolicy.NextAttempt(Quick, current, 3, Stable));
        }

        [Fact]
        public void TooManyQuickCrashesInARow_StayDown()
        {
            Assert.Equal(0, CrashRestartPolicy.NextAttempt(Quick, 3, 3, Stable));
            Assert.Equal(0, CrashRestartPolicy.NextAttempt(Quick, 7, 3, Stable));
        }

        [Fact]
        public void AStableRun_ResetsTheCounter_EvenAtTheLimit()
        {
            Assert.Equal(1, CrashRestartPolicy.NextAttempt(TimeSpan.FromHours(2), 3, 3, Stable));
        }

        [Fact]
        public void ExactlyStableUptime_CountsAsStable()
        {
            Assert.Equal(1, CrashRestartPolicy.NextAttempt(Stable, 3, 3, Stable));
        }

        [Fact]
        public void JustUnderStableUptime_DoesNotReset()
        {
            Assert.Equal(0, CrashRestartPolicy.NextAttempt(Stable - TimeSpan.FromSeconds(1), 3, 3, Stable));
        }

        [Fact]
        public void ACrashLoopThatNeverGetsStable_StopsAfterTheLimit()
        {
            int attempt = 0;
            int restarts = 0;
            for (int i = 0; i < 10; i++)
            {
                int next = CrashRestartPolicy.NextAttempt(Quick, attempt, 3, Stable);
                if (next == 0) break;
                attempt = next;
                restarts++;
            }
            Assert.Equal(3, restarts);
        }
    }
}
