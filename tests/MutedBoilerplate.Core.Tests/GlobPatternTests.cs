using MutedBoilerplate.Core.Matching;
using Xunit;

namespace MutedBoilerplate.Core.Tests;

public class GlobPatternTests
{
    [Theory]
    [InlineData("Log*", "LogInformation", true)]
    [InlineData("Log*", "Information", false)]
    [InlineData("Track*", "TrackEvent", true)]
    [InlineData("WriteLine|Write", "WriteLine", true)]
    [InlineData("WriteLine|Write", "Read", false)]
    [InlineData("ILogger*", "ILogger", true)]
    [InlineData("ILogger*", "Logger", false)]
    [InlineData("?ogger", "Logger", true)]
    [InlineData("", "anything", true)]
    public void Matches(string glob, string text, bool expected)
    {
        Assert.Equal(expected, GlobPattern.IsMatch(glob, text));
    }
}
