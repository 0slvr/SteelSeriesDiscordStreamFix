using System.Collections.Generic;
using Xunit;

namespace SteelSeriesDiscordStreamFix.Tests
{
    public class DiscordProcessMatcherTests
    {
        private static readonly HashSet<string> NoExtras = new HashSet<string>();

        [Theory]
        [InlineData("Discord")]
        [InlineData("DiscordPTB")]
        [InlineData("DiscordCanary")]
        [InlineData("DiscordDevelopment")]
        [InlineData("Vesktop")]
        [InlineData("Equibop")]
        [InlineData("Legcord")]
        [InlineData("ArmCord")]
        [InlineData("WebCord")]
        [InlineData("Dorion")]
        public void KnownClients_AreDiscord(string name)
        {
            Assert.True(DiscordProcessMatcher.IsDiscord(name, NoExtras));
        }

        [Theory]
        [InlineData("discord")]
        [InlineData("DISCORDPTB")]
        [InlineData("vesktop")]
        public void Matching_IsCaseInsensitive(string name)
        {
            Assert.True(DiscordProcessMatcher.IsDiscord(name, NoExtras));
        }

        [Theory]
        [InlineData("DiscordHelper")]
        [InlineData("DiscordSomethingNew")]
        [InlineData("Discord2")]
        [InlineData("chrome")]
        [InlineData("Spotify")]
        public void ProcessesThatOnlyStartWithDiscord_OrAreUnrelated_AreNotDiscord(string name)
        {
            Assert.False(DiscordProcessMatcher.IsDiscord(name, NoExtras));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void NullOrEmpty_IsNotDiscord(string name)
        {
            Assert.False(DiscordProcessMatcher.IsDiscord(name, NoExtras));
        }

        [Fact]
        public void ExtraNames_AreHonoured()
        {
            var extras = DiscordProcessMatcher.ParseExtraNames("MyFork");
            Assert.True(DiscordProcessMatcher.IsDiscord("MyFork", extras));
            Assert.True(DiscordProcessMatcher.IsDiscord("myfork", extras));
            Assert.False(DiscordProcessMatcher.IsDiscord("OtherFork", extras));
        }

        [Fact]
        public void NullExtraNames_AreAccepted()
        {
            Assert.True(DiscordProcessMatcher.IsDiscord("Discord", null));
            Assert.False(DiscordProcessMatcher.IsDiscord("MyFork", null));
        }

        [Fact]
        public void ParseExtraNames_SplitsOnCommasAndSemicolons_TrimsAndDropsExe()
        {
            var set = DiscordProcessMatcher.ParseExtraNames(" MyFork , Another.exe;Third.EXE ,, ;");
            Assert.Equal(3, set.Count);
            Assert.Contains("MyFork", set);
            Assert.Contains("Another", set);
            Assert.Contains("Third", set);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(" , ; ")]
        [InlineData(".exe")]
        public void ParseExtraNames_EmptyInput_GivesAnEmptySet(string raw)
        {
            Assert.Empty(DiscordProcessMatcher.ParseExtraNames(raw));
        }
    }
}
