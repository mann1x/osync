using Born2Code.Net;
using FluentAssertions;

namespace osync.Tests.UnitTests;

public class ThrottledStreamTests
{
    [Fact]
    public void Constructor_NullStream_ThrowsArgumentNullException()
    {
        var act = () => new ThrottledStream(null!, 1000);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NegativeBytesPerSecond_ThrowsArgumentOutOfRangeException()
    {
        using var ms = new MemoryStream();
        var act = () => new ThrottledStream(ms, -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_Infinite_Succeeds()
    {
        using var ms = new MemoryStream();
        using var ts = new ThrottledStream(ms, ThrottledStream.Infinite);
        ts.MaximumBytesPerSecond.Should().Be(0);
    }

    [Fact]
    public void Properties_DelegateToBaseStream()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var ms = new MemoryStream(data);
        using var ts = new ThrottledStream(ms, ThrottledStream.Infinite);

        ts.CanRead.Should().Be(ms.CanRead);
        ts.CanSeek.Should().Be(ms.CanSeek);
        ts.CanWrite.Should().Be(ms.CanWrite);
        ts.Length.Should().Be(ms.Length);
        ts.Position.Should().Be(ms.Position);
    }

    [Fact]
    public void Read_PassesThroughData()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        using var ms = new MemoryStream(data);
        using var ts = new ThrottledStream(ms, ThrottledStream.Infinite);

        var buffer = new byte[5];
        var bytesRead = ts.Read(buffer, 0, 5);

        bytesRead.Should().Be(5);
        buffer.Should().BeEquivalentTo(data);
    }

    [Fact]
    public void Write_PassesThroughData()
    {
        using var ms = new MemoryStream();
        using var ts = new ThrottledStream(ms, ThrottledStream.Infinite);

        var data = new byte[] { 10, 20, 30 };
        ts.Write(data, 0, data.Length);
        ts.Flush();

        ms.ToArray().Should().BeEquivalentTo(data);
    }

    [Fact]
    public void Seek_DelegatesToBaseStream()
    {
        using var ms = new MemoryStream(new byte[100]);
        using var ts = new ThrottledStream(ms, ThrottledStream.Infinite);

        ts.Seek(50, SeekOrigin.Begin);
        ts.Position.Should().Be(50);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void Throttle_SlowsDownReads()
    {
        // 100 bytes/sec, read 500 bytes in small chunks → should take several seconds
        var data = new byte[500];
        using var ms = new MemoryStream(data);
        using var ts = new ThrottledStream(ms, 100);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var buffer = new byte[10]; // small reads to trigger throttling
        int totalRead = 0;
        while (totalRead < 500)
        {
            var read = ts.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            totalRead += read;
        }
        sw.Stop();

        totalRead.Should().Be(500);
        // At 100 bytes/sec, 500 bytes should take ~5 seconds. Be generous: at least 2s
        sw.Elapsed.TotalSeconds.Should().BeGreaterThan(2.0);
    }
}
