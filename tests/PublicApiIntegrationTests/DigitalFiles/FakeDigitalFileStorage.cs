using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.DigitalFiles;

/// <summary>
/// In-memory stand-in for the Box folder used by the endpoint tests.
/// </summary>
public sealed class FakeDigitalFileStorage : IDigitalFileStorage
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Func<DigitalFileContent>> _contents = new();
    private readonly List<DigitalFileInfo> _files = new();

    public Exception? ListFailure { get; set; }

    public void AddFile(string id, string name, byte[] bytes, string? contentType = "application/octet-stream")
    {
        AddFile(id, name, bytes.Length, () => new DigitalFileContent(new MemoryStream(bytes), name, contentType, bytes.Length));
    }

    public void AddFile(string id, string name, long? size, Func<DigitalFileContent> open)
    {
        lock (_gate)
        {
            _files.RemoveAll(f => f.Id == id);
            _files.Add(new DigitalFileInfo(id, name, size));
            _contents[id] = open;
        }
    }

    /// <summary>The file stays linkable, but opening it fails as given.</summary>
    public void FailOpen(string id, Exception failure)
    {
        lock (_gate)
        {
            _contents[id] = () => throw failure;
        }
    }

    public Task<DigitalFileListing> ListFilesAsync(CancellationToken cancellationToken = default)
    {
        if (ListFailure is { } failure)
        {
            throw failure;
        }

        lock (_gate)
        {
            return Task.FromResult(new DigitalFileListing("eshop-digital-products", _files.ToArray(), false));
        }
    }

    public Task<DigitalFileContent> OpenReadAsync(string fileId, CancellationToken cancellationToken = default)
    {
        Func<DigitalFileContent>? open;
        lock (_gate)
        {
            _contents.TryGetValue(fileId, out open);
        }

        if (open is null)
        {
            throw new DigitalFileNotFoundException(fileId);
        }

        return Task.FromResult(open());
    }
}

/// <summary>
/// Returns <paramref name="head"/>, then never sends another byte (until cancelled) — a stalled Box download.
/// </summary>
public sealed class StallingStream : Stream
{
    private readonly byte[] _head;
    private int _position;

    public StallingStream(byte[] head) => _head = head;

    public bool Disposed { get; private set; }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < _head.Length)
        {
            var count = Math.Min(buffer.Length, _head.Length - _position);
            _head.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
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
