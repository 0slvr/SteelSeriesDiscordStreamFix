using Xunit;

namespace SteelSeriesDiscordStreamFix.Tests
{
    public class HideOwnershipTests
    {
        // ---------- ShouldSkip ----------

        [Theory]
        [InlineData(false, false, true)]   // option off, we hid nothing: nothing to do
        [InlineData(false, true, false)]   // option off, but we hid them: bring them back
        [InlineData(true, false, false)]   // option on: apply
        [InlineData(true, true, false)]
        public void ShouldSkip(bool disable, bool weHid, bool expected)
        {
            Assert.Equal(expected, HideOwnership.ShouldSkip(disable, weHid));
        }

        // ---------- ShouldClaimBeforeHiding ----------

        [Theory]
        [InlineData(true, true, false, true)]    // something to hide, not ours yet: claim before hiding
        [InlineData(true, false, false, false)]  // user already hid them some other way: NOT ours
        [InlineData(true, true, true, false)]    // already ours
        [InlineData(false, true, false, false)]  // option off: never claim
        [InlineData(false, false, false, false)]
        public void ShouldClaimBeforeHiding(bool disable, bool somethingToHide, bool weHid, bool expected)
        {
            Assert.Equal(expected, HideOwnership.ShouldClaimBeforeHiding(disable, somethingToHide, weHid));
        }

        // ---------- OwnershipAfterApply: success ----------

        [Fact]
        public void Success_ShowingDevices_ReleasesOwnership()
        {
            Assert.False(HideOwnership.OwnershipAfterApply(
                disable: false, somethingToHide: false, weHidDevices: true,
                succeeded: true, claimedNow: false, devicesStillVisible: false));
        }

        [Fact]
        public void Success_HidingDevices_WeHidThem_StaysOurs()
        {
            Assert.True(HideOwnership.OwnershipAfterApply(
                disable: true, somethingToHide: true, weHidDevices: true,
                succeeded: true, claimedNow: true, devicesStillVisible: false));
        }

        [Fact]
        public void Success_UserHadAlreadyHiddenThem_WeDoNotTakeOwnership()
        {
            Assert.False(HideOwnership.OwnershipAfterApply(
                disable: true, somethingToHide: false, weHidDevices: false,
                succeeded: true, claimedNow: false, devicesStillVisible: false));
        }

        [Fact]
        public void Success_AlreadyOurs_NothingToHideThisTime_StaysOurs()
        {
            Assert.True(HideOwnership.OwnershipAfterApply(
                disable: true, somethingToHide: false, weHidDevices: true,
                succeeded: true, claimedNow: false, devicesStillVisible: false));
        }

        // ---------- OwnershipAfterApply: failure ----------

        [Fact]
        public void Failure_ClaimMadeNow_DevicesStillVisible_ClaimIsGivenBack()
        {
            Assert.False(HideOwnership.OwnershipAfterApply(
                disable: true, somethingToHide: true, weHidDevices: true,
                succeeded: false, claimedNow: true, devicesStillVisible: true));
        }

        [Fact]
        public void Failure_ClaimMadeNow_ButSomeDevicesGotHidden_ClaimIsKept()
        {
            Assert.True(HideOwnership.OwnershipAfterApply(
                disable: true, somethingToHide: true, weHidDevices: true,
                succeeded: false, claimedNow: true, devicesStillVisible: false));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Failure_NoClaimMadeNow_OwnershipIsUnchanged(bool weHid)
        {
            Assert.Equal(weHid, HideOwnership.OwnershipAfterApply(
                disable: true, somethingToHide: true, weHidDevices: weHid,
                succeeded: false, claimedNow: false, devicesStillVisible: true));
        }

        [Fact]
        public void Failure_WhileShowing_KeepsOwnershipSoTheNextTryCanRestore()
        {
            Assert.True(HideOwnership.OwnershipAfterApply(
                disable: false, somethingToHide: false, weHidDevices: true,
                succeeded: false, claimedNow: false, devicesStillVisible: false));
        }

        [Fact]
        public void RepeatedFailures_NeverKeepAClaimThatWasNotHonoured()
        {
            // Regression: the claim used to be given back only after the FIRST failed attempt.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                bool weHid = false;
                bool claimedNow = HideOwnership.ShouldClaimBeforeHiding(true, true, weHid);
                if (claimedNow) weHid = true;

                bool owned = HideOwnership.OwnershipAfterApply(true, true, weHid, false, claimedNow, devicesStillVisible: true);
                Assert.False(owned);
            }
        }
    }
}
