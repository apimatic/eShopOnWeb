using System.Net;
using System.Net.Http;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// Covers the two Advanced Billing handlers composed the way
/// <see cref="Microsoft.eShopWeb.Infrastructure.Billing.MaxioBillingServiceCollectionExtensions"/>
/// composes them: the address rewrite outermost, retries inside it.
/// </summary>
public class MaxioHttpPipelineTests
{
    [Fact]
    public async Task AppliesAPathPrefixedBaseAddressExactlyOnceAcrossRetries()
    {
        // Rewriting inside the retry loop would turn /billing/site.json into /billing/billing/site.json
        // the second time round.
        var recorder = new UrlRecordingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        var pipeline = new MaxioBaseAddressHandler(new Uri("https://gateway.internal/billing/"))
        {
            InnerHandler = new MaxioResilienceHandler(
                maxConcurrentRequests: 4,
                maxRetries: 3,
                baseDelay: TimeSpan.Zero,
                NullLogger<MaxioResilienceHandler>.Instance)
            {
                InnerHandler = recorder,
            },
        };

        using var invoker = new HttpMessageInvoker(pipeline);
        var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://acme.chargify.com/site.json"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new[] { "https://gateway.internal/billing/site.json", "https://gateway.internal/billing/site.json" },
            recorder.Urls);
    }

    [Fact]
    public async Task LeavesTheEventsHostAloneWhenTheApiAddressIsOverridden()
    {
        var recorder = new UrlRecordingHandler(HttpStatusCode.OK);

        var handler = new MaxioBaseAddressHandler(new Uri("https://gateway.internal/"))
        {
            InnerHandler = recorder,
        };

        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://events.chargify.com/acme/events.json"),
            CancellationToken.None);

        Assert.Equal(new[] { "https://events.chargify.com/acme/events.json" }, recorder.Urls);
    }

    private sealed class UrlRecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode[] _script;
        private readonly List<string> _urls = new();
        private int _calls;

        public UrlRecordingHandler(params HttpStatusCode[] script) => _script = script;

        public string[] Urls
        {
            get
            {
                lock (_urls)
                {
                    return _urls.ToArray();
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (_urls)
            {
                _urls.Add(request.RequestUri!.ToString());
            }

            var index = Math.Min(_calls++, _script.Length - 1);
            return Task.FromResult(new HttpResponseMessage(_script[index]));
        }
    }
}
