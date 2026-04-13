using FluentAssertions;

namespace osync.Tests.UnitTests;

public class BufferedPipeStreamTests
{
    [Fact]
    public async Task WriteAndRead_Roundtrip_DataPreserved()
    {
        using var stream = new BufferedPipeStream(maxBufferSize: 1024);
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        await stream.WriteAsync(data, 0, data.Length);
        stream.CompleteWriting();

        var buffer = new byte[100];
        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

        bytesRead.Should().Be(10);
        buffer[..10].Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task PartialReads_AllDataRecovered()
    {
        using var stream = new BufferedPipeStream(maxBufferSize: 1024);
        var data = new byte[100];
        for (int i = 0; i < 100; i++) data[i] = (byte)(i % 256);

        await stream.WriteAsync(data, 0, data.Length);
        stream.CompleteWriting();

        var result = new List<byte>();
        var buffer = new byte[30];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            result.AddRange(buffer[..bytesRead]);
        }

        result.Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task MultipleWrites_ReadInOrder()
    {
        using var stream = new BufferedPipeStream(maxBufferSize: 1024);

        await stream.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3);
        await stream.WriteAsync(new byte[] { 4, 5, 6 }, 0, 3);
        await stream.WriteAsync(new byte[] { 7, 8, 9 }, 0, 3);
        stream.CompleteWriting();

        var buffer = new byte[100];
        var result = new List<byte>();
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            result.AddRange(buffer[..bytesRead]);
        }

        result.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
    }

    [Fact]
    public async Task CompleteWriting_ReadReturnsZero()
    {
        using var stream = new BufferedPipeStream(maxBufferSize: 1024);
        stream.CompleteWriting();

        var buffer = new byte[10];
        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        bytesRead.Should().Be(0);
    }

    [Fact]
    public async Task SetException_ReadThrows()
    {
        using var stream = new BufferedPipeStream(maxBufferSize: 1024);
        var expectedException = new IOException("Test error");
        stream.SetException(expectedException);

        var buffer = new byte[10];
        var act = async () => await stream.ReadAsync(buffer, 0, buffer.Length);
        await act.Should().ThrowAsync<IOException>().WithMessage("Test error");
    }

    [Fact]
    public async Task SetException_WriteThrows()
    {
        using var stream = new BufferedPipeStream(maxBufferSize: 1024);
        var expectedException = new IOException("Write error");
        stream.SetException(expectedException);

        var act = async () => await stream.WriteAsync(new byte[] { 1 }, 0, 1);
        await act.Should().ThrowAsync<IOException>().WithMessage("Write error");
    }

    [Fact]
    public async Task Properties_ReportCorrectCapabilities()
    {
        using var stream = new BufferedPipeStream(maxBufferSize: 1024);

        stream.CanRead.Should().BeTrue();
        stream.CanWrite.Should().BeTrue();
        stream.CanSeek.Should().BeFalse();
    }

    [Fact]
    public async Task ReadBeforeWrite_BlocksUntilDataAvailable()
    {
        using var stream = new BufferedPipeStream(maxBufferSize: 1024);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Start a read that will block
        var readTask = Task.Run(async () =>
        {
            var buffer = new byte[10];
            return await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
        });

        // Give the read a moment to start blocking
        await Task.Delay(50);
        readTask.IsCompleted.Should().BeFalse("read should block waiting for data");

        // Write data to unblock
        await stream.WriteAsync(new byte[] { 42 }, 0, 1);

        var bytesRead = await readTask;
        bytesRead.Should().Be(1);
    }
}
