using Xunit;

namespace SteelSeriesDiscordStreamFix.Tests
{
    public class LogRedactorTests
    {
        private const string Profile = @"C:\Users\Alice";

        [Fact]
        public void ReplacesTheExactPath()
        {
            var r = new LogRedactor(Profile);
            Assert.Equal(@"error in %USERPROFILE%\AppData\Local\x.cs",
                         r.Redact(@"error in C:\Users\Alice\AppData\Local\x.cs"));
        }

        [Theory]
        [InlineData(@"c:\users\alice\AppData")]
        [InlineData(@"C:\USERS\ALICE\AppData")]
        [InlineData(@"c:\Users\ALICE\AppData")]
        public void IsCaseInsensitive(string input)
        {
            var r = new LogRedactor(Profile);
            Assert.Equal(@"%USERPROFILE%\AppData", r.Redact(input));
        }

        [Fact]
        public void AlsoCoversForwardSlashes()
        {
            var r = new LogRedactor(Profile);
            Assert.Equal("%USERPROFILE%/AppData/log.txt", r.Redact("C:/Users/Alice/AppData/log.txt"));
        }

        [Fact]
        public void ReplacesEveryOccurrence()
        {
            var r = new LogRedactor(Profile);
            Assert.Equal(@"%USERPROFILE%\a and %USERPROFILE%\b",
                         r.Redact(@"C:\Users\Alice\a and c:\users\alice\b"));
        }

        [Fact]
        public void ProfilePathWithTrailingSeparator_StillWorks()
        {
            var r = new LogRedactor(@"C:\Users\Alice\");
            Assert.Equal(@"%USERPROFILE%\x", r.Redact(@"C:\Users\Alice\x"));
        }

        [Fact]
        public void ProfilePathWithSpace_IsEscapedCorrectly()
        {
            var r = new LogRedactor(@"C:\Users\Alice Smith");
            Assert.Equal(@"%USERPROFILE%\x", r.Redact(@"c:\users\alice smith\x"));
        }

        [Fact]
        public void DoesNotTouchOtherUsersOrUnrelatedText()
        {
            var r = new LogRedactor(Profile);
            const string other = @"C:\Users\Bob\file.txt and C:\Windows\System32";
            Assert.Equal(other, r.Redact(other));
        }

        [Fact]
        public void NullAndEmptyProfile_LeaveTheMessageUnchanged()
        {
            Assert.Equal("hello", new LogRedactor(null).Redact("hello"));
            Assert.Equal("hello", new LogRedactor("").Redact("hello"));
        }

        [Fact]
        public void NullMessage_ReturnsNull()
        {
            Assert.Null(new LogRedactor(Profile).Redact(null));
        }
    }
}
