using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.WikiTrendsEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WikimediaEventStreams;
using WikimediaEventStreams.Core.Configuration;
using WikimediaEventStreams.Core.ErrorResponse;
using WikimediaEventStreams.Core.Exceptions;

namespace PublicApiIntegrationTests.WikiTrendsEndpoints;

/// <summary>
/// Unit tests for WikiTrendsService — no network access; uses StubHandler.
/// </summary>
[TestClass]
public class WikiTrendsServiceTests
{
    // ── Stub infrastructure ──────────────────────────────────────────────────────────

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static WikimediaEventStreamsClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var options = new WikimediaEventStreamsClientOptions
        {
            Retry = RetryOptions.Default() with { MaxRetries = 0, Timeout = null },
            StreamReadTimeout = TimeSpan.FromSeconds(2)
        };
        return new WikimediaEventStreamsClient(new HttpClient(handler), options);
    }

    // ── SSE helpers ──────────────────────────────────────────────────────────────────

    private static string BuildSseBody(params JsonNode[] events)
    {
        var sb = new StringBuilder();
        foreach (var evt in events)
        {
            sb.Append("data: ").AppendLine(evt.ToJsonString());
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static HttpResponseMessage SseResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
        };

    private static HttpResponseMessage ErrorResponse(HttpStatusCode status) =>
        new(status)
        {
            Content = new StringContent("{\"error\":\"test\"}", Encoding.UTF8, "application/json")
        };

    // ── Fake repository ──────────────────────────────────────────────────────────────

    private sealed class FakeReadRepository<T> : IReadRepository<T> where T : class, IAggregateRoot
    {
        private readonly List<T> _items;
        public FakeReadRepository(IEnumerable<T> items) => _items = new List<T>(items);

        public Task<T?> GetByIdAsync<TId>(TId id, CancellationToken ct = default) where TId : notnull
            => Task.FromResult<T?>(null);

        public Task<T?> GetBySpecAsync(ISpecification<T> spec, CancellationToken ct = default)
            => Task.FromResult<T?>(_items.FirstOrDefault());

        public Task<TResult?> GetBySpecAsync<TResult>(ISpecification<T, TResult> spec, CancellationToken ct = default)
            => Task.FromResult<TResult?>(default);

        public Task<T?> FirstOrDefaultAsync(ISpecification<T> spec, CancellationToken ct = default)
            => Task.FromResult<T?>(_items.FirstOrDefault());

        public Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<T, TResult> spec, CancellationToken ct = default)
            => Task.FromResult<TResult?>(default);

        public Task<T?> SingleOrDefaultAsync(ISingleResultSpecification<T> spec, CancellationToken ct = default)
            => Task.FromResult<T?>(_items.SingleOrDefault());

        public Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<T, TResult> spec, CancellationToken ct = default)
            => Task.FromResult<TResult?>(default);

        public Task<List<T>> ListAsync(CancellationToken ct = default)
            => Task.FromResult(_items);

        public Task<List<T>> ListAsync(ISpecification<T> spec, CancellationToken ct = default)
            => Task.FromResult(_items);

        public Task<List<TResult>> ListAsync<TResult>(ISpecification<T, TResult> spec, CancellationToken ct = default)
            => Task.FromResult(new List<TResult>());

        public Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct = default)
            => Task.FromResult(_items.Count);

        public Task<int> CountAsync(CancellationToken ct = default)
            => Task.FromResult(_items.Count);

        public Task<bool> AnyAsync(ISpecification<T> spec, CancellationToken ct = default)
            => Task.FromResult(_items.Count > 0);

        public Task<bool> AnyAsync(CancellationToken ct = default)
            => Task.FromResult(_items.Count > 0);

        public async IAsyncEnumerable<T> AsAsyncEnumerable(ISpecification<T> spec)
        {
            foreach (var item in _items) yield return item;
            await Task.CompletedTask;
        }
    }

    private static WikiTrendsService CreateService(
        WikimediaEventStreamsClient client,
        IEnumerable<string>? brands = null,
        IEnumerable<string>? types = null)
    {
        var brandList = (brands ?? Array.Empty<string>())
            .Select(b => new CatalogBrand(b));
        var typeList = (types ?? Array.Empty<string>())
            .Select(t => new CatalogType(t));

        return new WikiTrendsService(
            client,
            new FakeReadRepository<CatalogBrand>(brandList),
            new FakeReadRepository<CatalogType>(typeList));
    }

    // ── Minimal valid event helper ────────────────────────────────────────────────────
    // Uses JsonObject so we can emit "$schema" (a key that can't appear in an anonymous type).

    private static JsonObject MakeEvent(string domain, string pageTitle, int revId = 1) =>
        new JsonObject
        {
            ["$schema"] = "/mediawiki/revision/create/2.0.0",
            ["database"] = domain.Contains("commons") ? "commonswiki" : "enwiki",
            ["dt"] = "2024-01-01T00:00:00Z",
            ["meta"] = new JsonObject
            {
                ["stream"] = "mediawiki.revision-create",
                ["domain"] = domain
            },
            ["page_id"] = 1,
            ["page_is_redirect"] = false,
            ["page_namespace"] = 0,
            ["page_title"] = pageTitle,
            ["rev_id"] = revId,
            ["rev_timestamp"] = "2024-01-01T00:00:00Z",
            ["performer"] = new JsonObject { ["user_text"] = "TestEditor" },
            ["rev_slots"] = new JsonObject
            {
                ["main"] = new JsonObject
                {
                    ["rev_slot_content_model"] = "wikitext",
                    ["rev_slot_sha1"] = "abc123",
                    ["rev_slot_size"] = 1024
                }
            }
        };

    // ── Tests ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task WatchAsync_MatchesEnWikipediaEditsWithCatalogTerms()
    {
        var sseBody = BuildSseBody(
            MakeEvent("en.wikipedia.org", ".NET Foundation history"),
            MakeEvent("en.wikipedia.org", "Unrelated page about cats"));
        var client = CreateClient(_ => SseResponse(sseBody));
        var service = CreateService(client, brands: [".NET"], types: []);

        var result = await service.WatchAsync(seconds: 5, CancellationToken.None);

        Assert.AreEqual(2, result.Received);
        Assert.AreEqual(1, result.Matches.Count);
        Assert.AreEqual("en.wikipedia.org", result.Matches[0].Wiki);
        Assert.AreEqual(".NET Foundation history", result.Matches[0].PageTitle);
        Assert.AreEqual("TestEditor", result.Matches[0].EditorName);
    }

    [TestMethod]
    public async Task WatchAsync_MatchesCommonsEditsWithCatalogTerms()
    {
        var sseBody = BuildSseBody(
            MakeEvent("commons.wikimedia.org", "File:Azure logo.svg", revId: 99));
        var client = CreateClient(_ => SseResponse(sseBody));
        var service = CreateService(client, brands: ["Azure"], types: []);

        var result = await service.WatchAsync(seconds: 5, CancellationToken.None);

        Assert.AreEqual(1, result.Received);
        Assert.AreEqual(1, result.Matches.Count);
        Assert.AreEqual("commons.wikimedia.org", result.Matches[0].Wiki);
        Assert.AreEqual(99, result.Matches[0].RevisionId);
    }

    [TestMethod]
    public async Task WatchAsync_LatestCommons_TracksLastFiveOnly()
    {
        // Build 7 commons events — should keep only last 5
        var sseBody = BuildSseBody(
            MakeEvent("commons.wikimedia.org", "File:A.jpg", 1),
            MakeEvent("commons.wikimedia.org", "File:B.jpg", 2),
            MakeEvent("commons.wikimedia.org", "File:C.jpg", 3),
            MakeEvent("commons.wikimedia.org", "File:D.jpg", 4),
            MakeEvent("commons.wikimedia.org", "File:E.jpg", 5),
            MakeEvent("commons.wikimedia.org", "File:F.jpg", 6),
            MakeEvent("commons.wikimedia.org", "File:G.jpg", 7));
        var client = CreateClient(_ => SseResponse(sseBody));
        var service = CreateService(client);

        var result = await service.WatchAsync(seconds: 5, CancellationToken.None);

        Assert.AreEqual(7, result.Received);
        Assert.AreEqual(5, result.LatestCommons.Count);
        Assert.AreEqual(3, result.LatestCommons[0].RevisionId, "oldest of last-5 should be revId 3");
        Assert.AreEqual(7, result.LatestCommons[4].RevisionId, "newest should be revId 7");
    }

    [TestMethod]
    public async Task WatchAsync_IgnoresNonTargetDomains()
    {
        var sseBody = BuildSseBody(
            MakeEvent("de.wikipedia.org", ".NET Framework article"));
        var client = CreateClient(_ => SseResponse(sseBody));
        var service = CreateService(client, brands: [".NET"], types: []);

        var result = await service.WatchAsync(seconds: 5, CancellationToken.None);

        Assert.AreEqual(1, result.Received);
        Assert.AreEqual(0, result.Matches.Count, "de.wikipedia.org should not match");
        Assert.AreEqual(0, result.LatestCommons.Count, "de.wikipedia.org is not commons");
    }

    [TestMethod]
    public async Task WatchAsync_SlotsAreReported()
    {
        var evt = new JsonObject
        {
            ["$schema"] = "/mediawiki/revision/create/2.0.0",
            ["database"] = "enwiki",
            ["dt"] = "2024-01-01T00:00:00Z",
            ["meta"] = new JsonObject { ["stream"] = "mediawiki.revision-create", ["domain"] = "en.wikipedia.org" },
            ["page_id"] = 1,
            ["page_is_redirect"] = false,
            ["page_namespace"] = 0,
            ["page_title"] = ".NET SDK",
            ["rev_id"] = 42,
            ["rev_timestamp"] = "2024-01-01T00:00:00Z",
            ["rev_slots"] = new JsonObject
            {
                ["main"] = new JsonObject
                {
                    ["rev_slot_content_model"] = "wikitext",
                    ["rev_slot_sha1"] = "sha1main",
                    ["rev_slot_size"] = 500
                }
            }
        };
        var sseBody = BuildSseBody(evt);
        var client = CreateClient(_ => SseResponse(sseBody));
        var service = CreateService(client, brands: [".NET"], types: []);

        var result = await service.WatchAsync(seconds: 5, CancellationToken.None);

        Assert.AreEqual(1, result.Matches.Count);
        Assert.AreEqual(1, result.Matches[0].Slots.Count);
        Assert.AreEqual("main", result.Matches[0].Slots[0].Name);
        Assert.AreEqual("wikitext", result.Matches[0].Slots[0].ContentModel);
        Assert.AreEqual(500, result.Matches[0].Slots[0].SizeBytes);
    }

    [TestMethod]
    public async Task WatchAsync_StreamError_OnHttpErrorOpening()
    {
        var client = CreateClient(_ => ErrorResponse(HttpStatusCode.ServiceUnavailable));
        var service = CreateService(client);

        var result = await service.WatchAsync(seconds: 5, CancellationToken.None);

        Assert.AreEqual(0, result.Received);
        StringAssert.StartsWith(result.StoppedBecause, "stream-error:");
        StringAssert.Contains(result.StoppedBecause, "503");
    }

    [TestMethod]
    public async Task WatchAsync_StreamError_OnNonEventStreamContentType()
    {
        // 200 OK but text/html — not a live stream
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>error</html>", Encoding.UTF8, "text/html")
        });
        var service = CreateService(client);

        var result = await service.WatchAsync(seconds: 5, CancellationToken.None);

        Assert.AreEqual(0, result.Received);
        StringAssert.StartsWith(result.StoppedBecause, "stream-error:");
        StringAssert.Contains(result.StoppedBecause, "text/html");
    }

    [TestMethod]
    public async Task WatchAsync_NoData_WhenStreamTimesOutBetweenFrames()
    {
        // A text/event-stream response that connects but never sends any SSE frame;
        // StreamReadTimeout (2s) fires => no-data
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StallingStreamContent()
        });
        var service = CreateService(client);

        var result = await service.WatchAsync(seconds: 10, CancellationToken.None);

        Assert.AreEqual("no-data", result.StoppedBecause);
    }

    [TestMethod]
    public async Task WatchAsync_CaseInsensitiveTermMatching()
    {
        var sseBody = BuildSseBody(
            MakeEvent("en.wikipedia.org", "visual studio code history"));
        var client = CreateClient(_ => SseResponse(sseBody));
        var service = CreateService(client, brands: ["Visual Studio"], types: []);

        var result = await service.WatchAsync(seconds: 5, CancellationToken.None);

        Assert.AreEqual(1, result.Matches.Count, "case-insensitive match should work");
    }

    [TestMethod]
    public async Task WatchAsync_StoppedBecause_TimeLimitWhenStreamEndsNormally()
    {
        var sseBody = BuildSseBody(MakeEvent("en.wikipedia.org", "Some page"));
        var client = CreateClient(_ => SseResponse(sseBody));
        var service = CreateService(client);

        var result = await service.WatchAsync(seconds: 5, CancellationToken.None);

        Assert.AreEqual("time-limit", result.StoppedBecause);
    }

    [TestMethod]
    public async Task WatchAsync_PropagatesCallerCancellation()
    {
        // Infinite SSE stream that never ends
        var infiniteBody = new InfiniteStreamContent();
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = infiniteBody
        });
        var service = CreateService(client);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        OperationCanceledException? thrown = null;
        try
        {
            await service.WatchAsync(seconds: 30, cts.Token);
        }
        catch (OperationCanceledException ex)
        {
            thrown = ex;
        }
        Assert.IsNotNull(thrown, "Expected OperationCanceledException (or subtype) to propagate");
    }

    // Shared blocking stream: ReadAsync blocks until the CancellationToken fires.
    // Used by both stalling-content types so the SDK's own timeouts and caller tokens
    // can cancel the read, rather than getting stuck in HttpContent's buffer loop.
    private sealed class BlockingStream : System.IO.Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, System.IO.SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return Task.FromCanceled<int>(ct);
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return ValueTask.FromCanceled<int>(ct);
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            return new ValueTask<int>(tcs.Task);
        }
    }

    // A text/event-stream that connects but never sends any SSE frame — triggers StreamReadTimeout.
    private sealed class StallingStreamContent : HttpContent
    {
        public StallingStreamContent()
        {
            Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        }

        protected override Task SerializeToStreamAsync(System.IO.Stream stream, System.Net.TransportContext? context)
            => Task.CompletedTask;

        // Return a non-buffering stream so ReadAsStreamAsync() doesn't stall in SerializeToStreamAsync.
        protected override Task<System.IO.Stream> CreateContentReadStreamAsync()
            => Task.FromResult<System.IO.Stream>(new BlockingStream());

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    // A text/event-stream that stalls indefinitely — for the caller-cancellation test.
    private sealed class InfiniteStreamContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(System.IO.Stream stream, System.Net.TransportContext? context)
            => Task.CompletedTask;

        protected override Task<System.IO.Stream> CreateContentReadStreamAsync()
            => Task.FromResult<System.IO.Stream>(new BlockingStream());

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
