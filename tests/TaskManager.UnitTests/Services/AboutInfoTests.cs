using TaskManager.Services;

namespace TaskManager.UnitTests.Services
{
    public class AboutInfoTests
    {
        [Theory]
        [InlineData("1.2.3+1a2b3c", "1.2.3", "1a2b3c")]
        [InlineData("0.1.0", "0.1.0", "")]
        [InlineData("", "", "")]
        [InlineData("1.2.3+a+b", "1.2.3", "a+b")]
        public void Parse_SplitsOnFirstPlus(string input, string expectedVersion, string expectedCommit)
        {
            var (version, commit) = AboutInfo.Parse(input);

            version.ShouldBe(expectedVersion);
            commit.ShouldBe(expectedCommit);
        }
    }
}
