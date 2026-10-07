using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.TrendEndpoints;

/// <summary>
/// Stands in for stream.wikimedia.org at the HttpClient seam: no network is touched.
/// </summary>
public sealed class FakeWikimediaHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public FakeWikimediaHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    public List<FakeEventStream> Streams { get; } = new();

    public List<HttpResponseMessage> Responses { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        var response = await _responder(request, cancellationToken);
        response.RequestMessage = request;
        lock (Responses)
            Responses.Add(response);
        return response;
    }

    /// <summary>A live stream: sends the frames, then stays open (silent) until the reader goes away.</summary>
    public static FakeWikimediaHandler LiveStream(params string[] frames) =>
        Stream(FakeEventStream.Ending.StayOpen, frames);

    public static FakeWikimediaHandler Stream(FakeEventStream.Ending ending, params string[] frames)
    {
        FakeWikimediaHandler? handler = null;
        handler = new FakeWikimediaHandler((_, _) =>
        {
            var stream = new FakeEventStream(frames, ending);
            lock (handler!.Streams)
                handler.Streams.Add(stream);
            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream") { CharSet = "utf-8" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        return handler;
    }

    public static FakeWikimediaHandler Answer(HttpStatusCode status, string mediaType, string body) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        }));

    public bool AllStreamsDisposed()
    {
        lock (Streams)
            return Streams.TrueForAll(s => s.IsDisposed);
    }
}

/// <summary>
/// A Server-Sent Events body that can stay open, end, or fail after its frames, and records when it is released.
/// </summary>
public sealed class FakeEventStream : Stream
{
    public enum Ending { StayOpen, Close, Fail }

    private readonly byte[] _payload;
    private readonly Ending _ending;
    private readonly CancellationTokenSource _disposed = new();
    private int _position;

    public FakeEventStream(IEnumerable<string> frames, Ending ending)
    {
        var builder = new StringBuilder();
        foreach (var frame in frames)
            builder.Append("event: message\n").Append("data: ").Append(frame).Append("\n\n");
        _payload = Encoding.UTF8.GetBytes(builder.ToString());
        _ending = ending;
    }

    public bool IsDisposed { get; private set; }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < _payload.Length)
        {
            var count = Math.Min(buffer.Length, _payload.Length - _position);
            _payload.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        switch (_ending)
        {
            case Ending.Close:
                return 0;
            case Ending.Fail:
                throw new IOException("The remote host closed the connection.");
            default:
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposed.Token))
                {
                    await Task.Delay(Timeout.Infinite, linked.Token);
                }
                return 0;
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            IsDisposed = true;
            _disposed.Cancel();
        }
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

/// <summary>
/// Builds mediawiki.revision-create event payloads with the wire names of the SDK's MediawikiRevisionCreate model.
/// </summary>
public static class RevisionEvents
{
    public static string Revision(string domain, string title, long revId = 1001, string user = "Example editor",
        IDictionary<string, object>? extraSlots = null, string database = "somewiki")
    {
        var slots = new Dictionary<string, object>
        {
            ["main"] = Slot("wikitext", 1234)
        };
        if (extraSlots is not null)
        {
            foreach (var (name, slot) in extraSlots)
                slots[name] = slot;
        }

        var payload = new Dictionary<string, object>
        {
            ["$schema"] = "/mediawiki/revision/create/2.0.0",
            ["meta"] = new Dictionary<string, object>
            {
                ["stream"] = "mediawiki.revision-create",
                ["domain"] = domain,
                ["uri"] = $"https://{domain}/wiki/{title}"
            },
            ["database"] = database,
            ["dt"] = "2026-10-08T10:00:00Z",
            ["page_id"] = 42,
            ["page_is_redirect"] = false,
            ["page_namespace"] = 0,
            ["page_title"] = title,
            ["performer"] = new Dictionary<string, object> { ["user_text"] = user, ["user_is_bot"] = false },
            ["rev_id"] = revId,
            ["rev_timestamp"] = "2026-10-08T10:00:00Z",
            ["rev_slots"] = slots
        };
        return JsonSerializer.Serialize(payload);
    }

    public static Dictionary<string, object> Slot(string contentModel, long size) => new()
    {
        ["rev_slot_content_model"] = contentModel,
        ["rev_slot_sha1"] = "0123456789abcdef",
        ["rev_slot_size"] = size,
        ["rev_slot_origin_rev_id"] = 1
    };
}
