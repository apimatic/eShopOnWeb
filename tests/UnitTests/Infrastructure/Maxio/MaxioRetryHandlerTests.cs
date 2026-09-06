using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioRetryHandlerTests
{
    private static MaxioOptions Options(int retries = 3) => new()
    {
        ApiKey = "k",
        Subdomain = "acme",
        ProductFamilyHandle = "plans",
        MaxRetryAttempts = retries,
        RetryBaseDelayMilliseconds = 1
    };

    private static HttpClient Client(CountingHandler inner, MaxioOptions? options = null)
    {
        var retry = new MaxioRetryHandler(
            new StaticOptionsMonitor<MaxioOptions>(options ?? Options()),
            NullLogger<MaxioRetryHandler>.Instance)
        {
            InnerHandler = inner
        };

        return new HttpClient(retry) { BaseAddress = new Uri("https://acme.chargify.com/") };
    }

    [Fact]
    public async Task RetriesAGetThatFailsWithAServerError()
    {
        var inner = new CountingHandler(HttpStatusCode.InternalServerError);

        var response = await Client(inner).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(4, inner.Calls); // the initial attempt plus MaxRetryAttempts
    }

    [Fact]
    public async Task StopsRetryingAsSoonAsARequestSucceeds()
    {
        var inner = new CountingHandler(HttpStatusCode.ServiceUnavailable, succeedFromCall: 3);

        var response = await Client(inner).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task DoesNotRetryAtAllWhenRetriesAreDisabled()
    {
        var inner = new CountingHandler(HttpStatusCode.InternalServerError);

        await Client(inner, Options(retries: 0)).GetAsync("site.json");

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task DoesNotRetryAPostAfterAnAmbiguousServerError()
    {
        // A 500 on POST /subscriptions.json may mean the subscription was created anyway; replaying
        // it could enrol the shopper twice.
        var inner = new CountingHandler(HttpStatusCode.InternalServerError);

        await Client(inner).PostAsync("subscriptions.json", JsonContent.Create(new { subscription = new { product_handle = "eshop-pro" } }));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task RetriesAPostThatWasThrottled()
    {
        // 429 means Maxio refused the request outright, so replaying it cannot duplicate anything.
        var inner = new CountingHandler(HttpStatusCode.TooManyRequests, succeedFromCall: 2);

        var response = await Client(inner).PostAsync(
            "subscriptions.json",
            JsonContent.Create(new { subscription = new { product_handle = "eshop-pro" } }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);

        // Proves the retried request carried its body again: resending an HttpRequestMessage with
        // content is the part most likely to break silently.
        Assert.Equal(2, inner.BodiesSeen.Count);
        Assert.All(inner.BodiesSeen, body => Assert.Contains("eshop-pro", body));
    }

    [Fact]
    public async Task DoesNotRetryAClientError()
    {
        var inner = new CountingHandler(HttpStatusCode.UnprocessableEntity);

        await Client(inner).GetAsync("site.json");

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task HonoursARetryAfterHeader()
    {
        var inner = new CountingHandler(HttpStatusCode.TooManyRequests, succeedFromCall: 2)
        {
            RetryAfterSeconds = 1
        };

        var started = DateTimeOffset.UtcNow;
        await Client(inner).GetAsync("site.json");
        var elapsed = DateTimeOffset.UtcNow - started;

        // The exponential backoff here would be ~1ms, so anything near a second can only have come
        // from the Retry-After header.
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(900), $"waited only {elapsed.TotalMilliseconds}ms");
    }

    [Fact]
    public async Task RetriesAGetAfterAConnectionFailure()
    {
        var inner = new CountingHandler(HttpStatusCode.OK, succeedFromCall: 3)
        {
            ThrowUntilCall = 3
        };

        var response = await Client(inner).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task DoesNotRetryAPostAfterAConnectionFailure()
    {
        var inner = new CountingHandler(HttpStatusCode.OK) { ThrowUntilCall = 3 };

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Client(inner).PostAsync("subscriptions.json", JsonContent.Create(new { })));

        Assert.Equal(1, inner.Calls);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _failureStatus;
        private readonly int _succeedFromCall;
        private int _calls;

        public CountingHandler(HttpStatusCode failureStatus, int succeedFromCall = int.MaxValue)
        {
            _failureStatus = failureStatus;
            _succeedFromCall = succeedFromCall;
        }

        public int Calls => Volatile.Read(ref _calls);

        public int? RetryAfterSeconds { get; set; }

        public int ThrowUntilCall { get; set; }

        public List<string> BodiesSeen { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);

            if (request.Content is not null)
            {
                BodiesSeen.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            if (call < ThrowUntilCall)
            {
                throw new HttpRequestException("connection reset");
            }

            if (call >= _succeedFromCall)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var response = new HttpResponseMessage(_failureStatus);
            if (RetryAfterSeconds is { } seconds)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }

            return response;
        }
    }
}
