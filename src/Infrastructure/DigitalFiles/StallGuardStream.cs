using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.DigitalFiles;

namespace Microsoft.eShopWeb.Infrastructure.DigitalFiles;

/// <summary>
/// A read-only wrapper over a download body that turns every way the transfer can go wrong into a
/// <see cref="DigitalFileTransferException"/>:
/// no data for <c>stallTimeout</c> on a single read, a broken connection, or an end of stream that
/// arrives before the announced length. The caller's own cancellation passes through unchanged.
/// </summary>
public sealed class StallGuardStream : Stream
{
    private readonly Stream _inner;
    private readonly TimeSpan _stallTimeout;
    private readonly TimeProvider _timeProvider;
    private readonly long? _expectedLength;
    private long _bytesRead;

    public StallGuardStream(Stream inner, TimeSpan stallTimeout, TimeProvider timeProvider, long? expectedLength)
    {
        _inner = inner;
        _stallTimeout = stallTimeout;
        _timeProvider = timeProvider;
        _expectedLength = expectedLength;
    }

    public long BytesRead => _bytesRead;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        int read;
        try
        {
            // WaitAsync bounds the wait even if the inner stream ignores cancellation; cancelling
            // readCts afterwards tears the inner read (and its connection) down.
            read = await _inner.ReadAsync(buffer, readCts.Token).AsTask()
                .WaitAsync(_stallTimeout, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            readCts.Cancel();
            throw new DigitalFileTransferException(
                $"The file provider sent no data for {_stallTimeout.TotalSeconds:0} s after {_bytesRead} bytes; the download was abandoned.", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException or ObjectDisposedException)
        {
            throw new DigitalFileTransferException(
                $"The connection to the file provider broke after {_bytesRead} bytes.", ex);
        }

        if (read == 0)
        {
            if (_expectedLength is long expected && _bytesRead != expected)
            {
                throw new DigitalFileTransferException(
                    $"The file provider ended the transfer after {_bytesRead} of {expected} bytes.");
            }
            return 0;
        }

        _bytesRead += read;
        if (_expectedLength is long announced && _bytesRead > announced)
        {
            throw new DigitalFileTransferException(
                $"The file provider sent more than the {announced} bytes it announced.");
        }
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
