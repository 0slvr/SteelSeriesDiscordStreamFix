using System;

namespace SteelSeriesDiscordStreamFix
{
    internal enum RemuteDecision
    {
        /// <summary>Nothing to do (the session is muted).</summary>
        Ignore,
        /// <summary>Un-muted right after it opened: mute it again.</summary>
        Remute,
        /// <summary>Un-muted after the protection window: a deliberate user action, leave it alone.</summary>
        RespectUserUnmute,
        /// <summary>Un-muted again after too many automatic re-mutes: stop fighting (not remembered as a user choice).</summary>
        GiveUp
    }

    /// <summary>
    /// The decision logic of the Discord re-mute protection, free of any audio/COM code so it can be unit tested.
    /// </summary>
    internal static class RemutePolicy
    {
        public static RemuteDecision Decide(bool isMuted, TimeSpan sinceTracked, TimeSpan window, int remutesDone, int maxRemutes)
        {
            if (isMuted) return RemuteDecision.Ignore;
            if (sinceTracked > window) return RemuteDecision.RespectUserUnmute;
            if (remutesDone >= maxRemutes) return RemuteDecision.GiveUp;
            return RemuteDecision.Remute;
        }
    }
}
