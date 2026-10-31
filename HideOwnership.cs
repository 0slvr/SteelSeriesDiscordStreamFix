namespace SteelSeriesDiscordStreamFix
{
    /// <summary>
    /// Who owns the "Sonar Chat/Gaming are hidden" state. The app may only bring back devices IT hid, never
    /// devices the user hid some other way. Pure logic (no registry, no COM) so it can be unit tested.
    /// </summary>
    internal static class HideOwnership
    {
        /// <summary>True when there is nothing to do: the option is off and we never hid anything.</summary>
        public static bool ShouldSkip(bool disable, bool weHidDevices)
        {
            return !disable && !weHidDevices;
        }

        /// <summary>
        /// True when ownership must be written BEFORE hiding (so a power loss right after still restores them).
        /// Only when there really is something to hide: devices the user already hid are not ours.
        /// </summary>
        public static bool ShouldClaimBeforeHiding(bool disable, bool somethingToHide, bool weHidDevices)
        {
            return disable && somethingToHide && !weHidDevices;
        }

        /// <summary>The value of "we hid the devices" after an apply attempt.</summary>
        /// <param name="weHidDevices">The stored value at this point (after a claim made before hiding).</param>
        /// <param name="claimedNow">Ownership was claimed in this very apply.</param>
        /// <param name="devicesStillVisible">After a failed apply: at least one device is still visible.</param>
        public static bool OwnershipAfterApply(bool disable, bool somethingToHide, bool weHidDevices,
                                               bool succeeded, bool claimedNow, bool devicesStillVisible)
        {
            if (succeeded)
            {
                if (!disable) return false;              // everything is visible again, nothing is ours any more
                return somethingToHide || weHidDevices;  // we hid them now (or already owned them)
            }

            // Failed: do not keep a claim we cannot honour (every time, not only for the first failure).
            if (claimedNow && devicesStillVisible) return false;
            return weHidDevices;
        }
    }
}
