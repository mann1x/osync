using FluentAssertions;
using osync;
using Xunit;

namespace osync.Tests.UnitTests;

/// <summary>ps / psmonitor with a second server (a second -d) and psmonitor's unload errors.</summary>
public class PsSecondaryTests
{
    [Theory]
    [InlineData("ps -d a:22434 -d b:11434", "ps -d a:22434 --secondary b:11434")]
    [InlineData("psmonitor -d a:22434 -d b:11434 -L 2", "psmonitor -d a:22434 --secondary b:11434 -L 2")]
    [InlineData("psm -d a -d b", "psm -d a --secondary b")]
    [InlineData("monitor -d a --destination b", "monitor -d a --secondary b")]
    // ps takes its first server as a positional argument too
    [InlineData("ps a:22434 -d b:11434", "ps a:22434 --secondary b:11434")]
    [InlineData("ps -d b:11434 a:22434", "ps --secondary b:11434 a:22434")]
    // one server: nothing changes
    [InlineData("ps -d a:22434", "ps -d a:22434")]
    [InlineData("psmonitor -d a -L 5", "psmonitor -d a -L 5")]
    // other commands are left alone (PowerArgs reports their repeated -d)
    [InlineData("ls -d a -d b", "ls -d a -d b")]
    public void SecondDestinationBecomesSecondary(string input, string expected) =>
        string.Join(' ', Program.SecondDestinationAsSecondary(input.Split(' '))).Should().Be(expected);

    [Theory]
    [InlineData("""{"error":"model 'x' not found"}""", 404, "model 'x' not found")]
    [InlineData("not json", 500, "HTTP 500")]
    [InlineData("""{"status":"x"}""", 400, "HTTP 400")]
    public void ServerError_ReadsTheErrorField(string body, int status, string expected) =>
        RemoteServerMonitor.ServerError(body, status).Should().Be(expected);
}
