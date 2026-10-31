using System;

namespace SteelSeriesDiscordStreamFix
{
    /// <summary>When the crash handler may start a fresh copy. Pure logic so it can be unit tested.</summary>
    internal static class CrashRestartPolicy
    {
        /// <summary>
        /// Returns the attempt number of the next restart (1..maxAttempts), or 0 when the app must stay down.
        /// A process that ran for at least <paramref name="stableUptime"/> before crashing is a new problem, not
        /// part of a crash loop, so the counter starts again from 1.
        /// </summary>
        public static int NextAttempt(TimeSpan uptime, int currentAttempt, int maxAttempts, TimeSpan stableUptime)
        {
            int next = uptime >= stableUptime ? 1 : currentAttempt + 1;
            return next > maxAttempts ? 0 : next;
        }
    }
}
