using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.TrendEndpoints;

/// <summary>
/// A response body that serves scripted bytes, then ends, hangs until cancelled/disposed, or fails —
/// and records whether the SDK released (disposed) it.
/// </summary>
public sealed class ScriptedSseStream : Stream
{
    public enum Then { End, Hang, Fail }

    private readonly byte[] _data;
    private readonly Then _then;
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _position;

    public ScriptedSseStream(string body, Then then)
    {
        _data = System.Text.Encoding.UTF8.GetBytes(body);
        _then = then;
    }

    public bool IsDisposed => _disposed.Task.IsCompleted;

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(ScriptedSseStream));

        if (_position < _data.Length)
        {
            var count = Math.Min(buffer.Length, _data.Length - _position);
            _data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        switch (_then)
        {
            case Then.End:
                return 0;
            case Then.Fail:
                throw new IOException("The connection was reset by the peer.");
            default:
                await Task.WhenAny(Task.Delay(Timeout.Infinite, cancellationToken), _disposed.Task);
                cancellationToken.ThrowIfCancellationRequested();
                throw new IOException("The response was disposed while reading.");
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    protected override void Dispose(bool disposing)
    {
        _disposed.TrySetResult();
        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
