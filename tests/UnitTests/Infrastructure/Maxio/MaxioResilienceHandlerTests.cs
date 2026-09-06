using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioResilienceHandlerTests
{
    private static HttpClient CreateClient(CountingHandler inner, int maxConcurrentRequests = 4)
    {
        var handler = new MaxioResilienceHandler(new MaxioRequestThrottle(maxConcurrentRequests), NullLogger<MaxioResilienceHandler>.Instance)
        {
            InnerHandler = inner
        };

        return new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") };
    }

    [Fact]
    public async Task RetriesAThrottledResponse()
    {
        var inner = new CountingHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);

        var response = await CreateClient(inner).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task RetriesAServerError()
    {
        var inner = new CountingHandler(HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        var response = await CreateClient(inner).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task GivesUpAfterTheRetryBudgetAndReturnsTheLastResponse()
    {
        var inner = new CountingHandler(Enumerable.Repeat(HttpStatusCode.BadGateway, 10).ToArray());

        var response = await CreateClient(inner).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(4, inner.Calls);
    }

    [Fact]
    public async Task DoesNotRetryABusinessRuleRejection()
    {
        var inner = new CountingHandler(HttpStatusCode.UnprocessableEntity, HttpStatusCode.OK);

        var response = await CreateClient(inner).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task DoesNotRetryADuplicateSubmissionRejection()
    {
        var inner = new CountingHandler(HttpStatusCode.Conflict, HttpStatusCode.OK);

        var response = await CreateClient(inner).GetAsync("site.json");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task ReplaysTheRequestBodyOnEveryAttempt()
    {
        var inner = new CountingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        var response = await CreateClient(inner).PostAsync("subscriptions.json",
            new StringContent("""{"subscription":{"product_handle":"eshop-pro"}}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Bodies.Count);
        Assert.Equal(inner.Bodies[0], inner.Bodies[1]);
        Assert.All(inner.Bodies, body => Assert.Contains("eshop-pro", body));
    }

    [Fact]
    public async Task NeverExceedsTheConcurrencyBudget()
    {
        var inner = new CountingHandler(Enumerable.Repeat(HttpStatusCode.OK, 40).ToArray()) { Latency = TimeSpan.FromMilliseconds(20) };
        var client = CreateClient(inner, maxConcurrentRequests: 4);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.GetAsync("site.json")));

        Assert.True(inner.PeakConcurrency <= 4, $"peak concurrency was {inner.PeakConcurrency}");
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statusCodes;
        private readonly object _sync = new();
        private int _inFlight;

        public CountingHandler(params HttpStatusCode[] statusCodes) => _statusCodes = new Queue<HttpStatusCode>(statusCodes);

        public int Calls { get; private set; }
        public int PeakConcurrency { get; private set; }
        public List<string> Bodies { get; } = new();
        public TimeSpan Latency { get; set; } = TimeSpan.Zero;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                Calls++;
                _inFlight++;
                PeakConcurrency = Math.Max(PeakConcurrency, _inFlight);
            }

            try
            {
                if (request.Content is not null)
                {
                    var body = await request.Content.ReadAsStringAsync(cancellationToken);
                    lock (_sync)
                    {
                        Bodies.Add(body);
                    }
                }

                if (Latency > TimeSpan.Zero)
                {
                    await Task.Delay(Latency, cancellationToken);
                }

                HttpStatusCode statusCode;
                lock (_sync)
                {
                    statusCode = _statusCodes.Count > 0 ? _statusCodes.Dequeue() : HttpStatusCode.OK;
                }

                return new HttpResponseMessage(statusCode) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }
            finally
            {
                lock (_sync)
                {
                    _inFlight--;
                }
            }
        }
    }
}
