using System.Runtime.InteropServices;
using FluentAssertions;

namespace osync.Tests.UnitTests;

public class UpgradeTests
{
    private static ReleaseVersion V(string text)
    {
        ReleaseVersion.TryParse(text, out var v).Should().BeTrue(text);
        return v;
    }

    [Theory]
    [InlineData("v1.4.5", 1, 4, 5, null)]
    [InlineData("1.4.5", 1, 4, 5, null)]
    [InlineData("v1.4.6-dev.93", 1, 4, 6, "dev.93")]
    public void Parse_Versions(string text, int major, int minor, int patch, string? pre)
    {
        V(text).Should().Be(new ReleaseVersion(major, minor, patch, pre));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("v1.4")]
    [InlineData("")]
    public void Parse_RejectsNonVersions(string text)
    {
        ReleaseVersion.TryParse(text, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("v1.4.6", "v1.4.5")]
    [InlineData("v1.10.0", "v1.9.9")]
    [InlineData("v1.4.6", "v1.4.6-dev.93")]
    [InlineData("v1.4.6-dev.93", "v1.4.5")]
    [InlineData("v1.4.6-dev.100", "v1.4.6-dev.99")]
    public void Compare_Newer(string newer, string older)
    {
        (V(newer) > V(older)).Should().BeTrue();
        (V(older) < V(newer)).Should().BeTrue();
    }

    [Fact]
    public void Compare_Equal()
    {
        V("v1.4.6-dev.93").CompareTo(V("1.4.6-dev.93")).Should().Be(0);
        V("v1.4.5").ToString().Should().Be("v1.4.5");
        V("1.4.6-dev.93").ToString().Should().Be("v1.4.6-dev.93");
    }

    private const string ReleasesJson = """
        [
          { "tag_name": "v1.4.7-dev.120", "prerelease": true, "draft": false, "html_url": "https://github.com/mann1x/osync/releases/tag/v1.4.7-dev.120",
            "assets": [ { "name": "osync", "browser_download_url": "https://example/dev/osync", "size": 10, "digest": "sha256:abc" } ] },
          { "tag_name": "v1.4.8", "draft": true, "assets": [] },
          { "tag_name": "nightly", "prerelease": false, "assets": [] },
          { "tag_name": "v1.4.6", "prerelease": false, "draft": false, "html_url": "https://github.com/mann1x/osync/releases/tag/v1.4.6",
            "assets": [
              { "name": "osync", "browser_download_url": "https://example/osync", "size": 42 },
              { "name": "osync.exe", "browser_download_url": "https://example/osync.exe", "size": 43 }
            ] },
          { "tag_name": "v1.4.5", "prerelease": false, "assets": [] }
        ]
        """;

    [Fact]
    public void Releases_SkipDraftsAndNonVersionTags()
    {
        var releases = OsyncReleases.Parse(ReleasesJson);

        releases.Select(r => r.Tag).Should().Equal("v1.4.7-dev.120", "v1.4.6", "v1.4.5");
        var stable = releases.Single(r => r.Tag == "v1.4.6");
        stable.PreRelease.Should().BeFalse();
        stable.Assets.Should().HaveCount(2);
        stable.Assets[0].Should().Be(new ReleaseAsset("osync", "https://example/osync", 42, null));
        releases[0].Assets[0].Digest.Should().Be("sha256:abc");
    }

    [Fact]
    public void Newest_IgnoresPreReleasesUnlessAsked()
    {
        var releases = OsyncReleases.Parse(ReleasesJson);

        OsyncReleases.Newest(releases, includePreReleases: false)!.Tag.Should().Be("v1.4.6");
        OsyncReleases.Newest(releases, includePreReleases: true)!.Tag.Should().Be("v1.4.7-dev.120");
    }

    [Fact]
    public void Newest_StableReleaseBeatsItsOwnPreRelease()
    {
        var releases = OsyncReleases.Parse("""
            [ { "tag_name": "v1.4.6-dev.93", "prerelease": true, "assets": [] },
              { "tag_name": "v1.4.6", "prerelease": false, "assets": [] } ]
            """);

        OsyncReleases.Newest(releases, includePreReleases: true)!.Tag.Should().Be("v1.4.6");
    }

    [Fact]
    public void Releases_TagWithSuffixIsAPreReleaseEvenWhenNotFlagged()
    {
        var releases = OsyncReleases.Parse("""[ { "tag_name": "v2.0.0-rc.1", "prerelease": false, "assets": [] } ]""");

        releases.Single().PreRelease.Should().BeTrue();
        OsyncReleases.Newest(releases, includePreReleases: false).Should().BeNull();
    }

    [Fact]
    public void AssetName_MatchesTheReleaseBinaries()
    {
        OsyncReleases.AssetName(OSPlatform.Windows, Architecture.X64).Should().Be("osync.exe");
        OsyncReleases.AssetName(OSPlatform.Linux, Architecture.X64).Should().Be("osync");
        OsyncReleases.AssetName(OSPlatform.OSX, Architecture.Arm64).Should().Be("osync-macos-arm64");
        OsyncReleases.AssetName(OSPlatform.OSX, Architecture.X64).Should().Be("osync-macos-x64");
        OsyncReleases.AssetName(OSPlatform.Linux, Architecture.Arm64).Should().BeNull();
    }

    [Theory]
    [InlineData("osync v1.4.5 (b20261009-1200)\n", "v1.4.5")]
    [InlineData("osync v1.4.6-dev.93 (b20261016-1000)", "v1.4.6-dev.93")]
    [InlineData("something else", null)]
    public void ParseVersionOutput(string output, string? expected)
    {
        OsyncReleases.ParseVersionOutput(output)?.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("", true, true)]
    [InlineData("", false, false)]
    [InlineData("y", false, true)]
    [InlineData("YES", false, true)]
    [InlineData("n", true, false)]
    public void AskYesNo_EmptyAnswerTakesTheDefault(string answer, bool defaultYes, bool expected)
    {
        var output = new StringWriter();

        UpgradeCommand.AskYesNo(new StringReader(answer + "\n"), output, "Go?", defaultYes).Should().Be(expected);
        output.ToString().Should().StartWith(defaultYes ? "Go? (Y/n): " : "Go? (y/N): ");
    }

    [Fact]
    public void AskYesNo_EndOfInputIsNoAnswer()
    {
        UpgradeCommand.AskYesNo(new StringReader(""), new StringWriter(), "Go?", defaultYes: true).Should().BeNull();
    }

    [Fact]
    public void Replace_PutsTheNewBinaryInPlace()
    {
        var dir = Directory.CreateTempSubdirectory("osync-upgrade-tests-").FullName;
        try
        {
            var target = Path.Combine(dir, "osync");
            var downloaded = Path.Combine(dir, "osync.new");
            File.WriteAllText(target, "old");
            File.WriteAllText(downloaded, "new");

            UpgradeCommand.Replace(downloaded, target);

            File.ReadAllText(target).Should().Be("new");
            File.Exists(downloaded).Should().BeFalse();
            Directory.GetFiles(dir).Should().ContainSingle("the moved-aside binary is deleted when it is not running");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, "v1.4.5", false)]
    [InlineData(false, "v1.4.6-dev.131", true)]
    [InlineData(true, "v1.4.5", true)]
    public void Channel_FollowsTheRunningOsync(bool preFlag, string running, bool expected)
    {
        OsyncReleases.IncludePreReleases(preFlag, V(running)).Should().Be(expected);
    }

    [Fact]
    public void Newest_DevChannelTakesAPreReleaseNewerThanTheLatestRelease()
    {
        var releases = OsyncReleases.Parse("""
            [ { "tag_name": "v1.4.6-dev.131", "prerelease": true, "assets": [] },
              { "tag_name": "v1.4.5", "prerelease": false, "assets": [] },
              { "tag_name": "v1.4.4", "prerelease": false, "assets": [] } ]
            """);

        OsyncReleases.Newest(releases, OsyncReleases.IncludePreReleases(false, V("v1.4.6-dev.131")))!.Tag.Should().Be("v1.4.6-dev.131");
        OsyncReleases.Newest(releases, OsyncReleases.IncludePreReleases(false, V("v1.4.4")))!.Tag.Should().Be("v1.4.5");
    }

    [Fact]
    public void MoveWithRetry_WaitsWhileTheFileIsInUse()
    {
        int calls = 0;
        var log = new StringWriter();

        UpgradeCommand.MoveWithRetry("a", "b", overwrite: false, log, attempts: 5, delayMs: 1, move: (_, _, _) =>
        {
            if (++calls < 3) throw new IOException("The process cannot access the file because it is being used by another process.");
        });

        calls.Should().Be(3);
        log.ToString().Should().Contain("Waiting for a to be released");
    }

    [Fact]
    public void MoveWithRetry_GivesUpNamingTheFile()
    {
        var act = () => UpgradeCommand.MoveWithRetry("/x/osync.exe", "/x/osync.exe.old", overwrite: false, null, attempts: 2, delayMs: 1,
            move: (_, _, _) => throw new IOException("in use"));

        act.Should().Throw<IOException>().WithMessage("cannot rename /x/osync.exe to osync.exe.old: in use*");
    }

    [Fact]
    public void MoveWithRetry_DoesNotRetryAMissingFile()
    {
        int calls = 0;
        var act = () => UpgradeCommand.MoveWithRetry("a", "b", overwrite: false, null, attempts: 5, delayMs: 1,
            move: (_, _, _) => { calls++; throw new FileNotFoundException("gone"); });

        act.Should().Throw<FileNotFoundException>();
        calls.Should().Be(1);
    }
}
