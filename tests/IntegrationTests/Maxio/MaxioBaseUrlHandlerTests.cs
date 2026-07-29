using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

public class MaxioBaseUrlHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? CapturedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Theory]
    // The override's scheme/host/port and base path replace the SDK-derived address; path + query are preserved.
    [InlineData("https://proxy.internal:8443/maxio", "https://cp-exp-1.chargify.com/subscriptions.json?page=1", "https://proxy.internal:8443/maxio/subscriptions.json?page=1")]
    [InlineData("https://alt.example.com", "https://cp-exp-1.chargify.com/customers/lookup.json?reference=abc", "https://alt.example.com/customers/lookup.json?reference=abc")]
    [InlineData("http://localhost:9999", "https://cp-exp-1.chargify.com/products.json", "http://localhost:9999/products.json")]
    public async Task Rewrites_base_address_preserving_path_and_query(string baseUrl, string requestUrl, string expected)
    {
        var capturing = new CapturingHandler();
        var handler = new MaxioBaseUrlHandler(new Uri(baseUrl)) { InnerHandler = capturing };
        using var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, requestUrl), CancellationToken.None);

        Assert.NotNull(capturing.CapturedUri);
        Assert.Equal(expected, capturing.CapturedUri!.ToString());
    }
}
