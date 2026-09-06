using System.Diagnostics;
using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioResilienceHandlerTests
{
    private static readonly TimeSpan NoDelay = TimeSpan.Zero;

    private static HttpMessageInvoker Invoker(
        StubHandler inner,
        int maxRetries = 3,
        int maxConcurrentRequests = 8,
        TimeSpan? baseDelay = null)
    {
        var handler = new MaxioResilienceHandler(
            maxConcurrentRequests,
            maxRetries,
            baseDelay ?? NoDelay,
            NullLogger<MaxioResilienceHandler>.Instance)
        {
            InnerHandler = inner,
        };

        return new HttpMessageInvoker(handler);
    }

    private static HttpRequestMessage Request(HttpMethod method, HttpContent? content = null) =>
        new(method, "https://acme.chargify.com/subscriptions.json") { Content = content };

    [Fact]
    public async Task PassesASuccessfulResponseStraightThrough()
    {
        var inner = new StubHandler(HttpStatusCode.OK);
        using var invoker = Invoker(inner);

        var response = await invoker.SendAsync(Request(HttpMethod.Get), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData((HttpStatusCode)429)]
    public async Task RetriesAReadThatFailedTransiently(HttpStatusCode status)
    {
        var inner = new StubHandler(status, HttpStatusCode.OK);
        using var invoker = Invoker(inner);

        var response = await invoker.SendAsync(Request(HttpMethod.Get), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task GivesUpAfterTheConfiguredNumberOfRetries()
    {
        var inner = new StubHandler(Enumerable.Repeat(HttpStatusCode.ServiceUnavailable, 10).ToArray());
        using var invoker = Invoker(inner, maxRetries: 2);

        var response = await invoker.SendAsync(Request(HttpMethod.Get), CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Calls); // the original attempt plus two retries
    }

    [Fact]
    public async Task DoesNotRetryAWrite()
    {
        // A replayed POST could enroll a shopper twice if the first attempt landed and only the response
        // was lost. Replay of writes is resolved a layer up, against the billing system's own records.
        var inner = new StubHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var invoker = Invoker(inner);

        var response = await invoker.SendAsync(Request(HttpMethod.Post, new StringContent("{}")), CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task DoesNotRetryAResponseThatWillNotChange(HttpStatusCode status)
    {
        var inner = new StubHandler(status, HttpStatusCode.OK);
        using var invoker = Invoker(inner);

        var response = await invoker.SendAsync(Request(HttpMethod.Get), CancellationToken.None);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task RetriesAReadThatNeverReachedTheServer()
    {
        var inner = new StubHandler(new HttpRequestException("connection reset"), HttpStatusCode.OK);
        using var invoker = Invoker(inner);

        var response = await invoker.SendAsync(Request(HttpMethod.Get), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task SurfacesTheTransportFailureOnceRetriesAreExhausted()
    {
        var inner = new StubHandler(Enumerable.Repeat<object>(new HttpRequestException("down"), 5).ToArray());
        using var invoker = Invoker(inner, maxRetries: 1);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => invoker.SendAsync(Request(HttpMethod.Get), CancellationToken.None));

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task WaitsAsLongAsAdvancedBillingAsksWhenItThrottlesUs()
    {
        var throttled = new HttpResponseMessage((HttpStatusCode)429);
        throttled.Headers.Add("Retry-After", "1");

        var inner = new StubHandler(throttled, new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = Invoker(inner, maxRetries: 1);

        var stopwatch = Stopwatch.StartNew();
        var response = await invoker.SendAsync(Request(HttpMethod.Get), CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromMilliseconds(800),
            $"expected to honour Retry-After, waited only {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task HoldsRequestsBackToTheConfiguredConcurrency()
    {
        var inner = new StubHandler(HttpStatusCode.OK) { Latency = TimeSpan.FromMilliseconds(20) };
        using var invoker = Invoker(inner, maxConcurrentRequests: 2);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            invoker.SendAsync(Request(HttpMethod.Get), CancellationToken.None)));

        Assert.True(inner.PeakConcurrency <= 2, $"peak concurrency was {inner.PeakConcurrency}");
        Assert.Equal(12, inner.Calls);
    }

    [Fact]
    public async Task StopsRetryingWhenTheCallerGivesUp()
    {
        var inner = new StubHandler(Enumerable.Repeat(HttpStatusCode.ServiceUnavailable, 10).ToArray());
        using var invoker = Invoker(inner, maxRetries: 5, baseDelay: TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => invoker.SendAsync(Request(HttpMethod.Get), cancellation.Token));
    }

    /// <summary>
    /// Replays a scripted sequence of responses and exceptions, recording how it was called.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly object[] _script;
        private int _calls;
        private int _inFlight;
        private int _peak;

        public StubHandler(params HttpStatusCode[] script)
            : this(script.Cast<object>().ToArray())
        {
        }

        public StubHandler(params object[] script)
        {
            _script = script;
        }

        public int Calls => Volatile.Read(ref _calls);

        public int PeakConcurrency => Volatile.Read(ref _peak);

        public TimeSpan Latency { get; init; } = TimeSpan.Zero;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var index = Interlocked.Increment(ref _calls) - 1;

            var inFlight = Interlocked.Increment(ref _inFlight);
            InterlockedMax(ref _peak, inFlight);

            try
            {
                if (Latency > TimeSpan.Zero)
                {
                    await Task.Delay(Latency, cancellationToken);
                }

                return _script[Math.Min(index, _script.Length - 1)] switch
                {
                    HttpStatusCode status => new HttpResponseMessage(status),
                    HttpResponseMessage response => response,
                    Exception failure => throw failure,
                    var other => throw new InvalidOperationException($"unsupported script entry {other}"),
                };
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value &&
                   Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }
}
