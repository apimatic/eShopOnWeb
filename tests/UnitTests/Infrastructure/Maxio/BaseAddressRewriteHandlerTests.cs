using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class BaseAddressRewriteHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? CapturedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<Uri> SendThrough(string baseUrl, string requestUrl)
    {
        var capturing = new CapturingHandler();
        var handler = new BaseAddressRewriteHandler(baseUrl) { InnerHandler = capturing };
        using var client = new HttpClient(handler);

        await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, requestUrl));
        return capturing.CapturedUri!;
    }

    [Fact]
    public async Task RewritesHostAndScheme_PreservingPathAndQuery()
    {
        var result = await SendThrough(
            "https://proxy.internal:8443",
            "https://cp-exp-8.chargify.com/subscriptions.json?page=1");

        Assert.Equal("https", result.Scheme);
        Assert.Equal("proxy.internal", result.Host);
        Assert.Equal(8443, result.Port);
        Assert.Equal("/subscriptions.json", result.AbsolutePath);
        Assert.Equal("?page=1", result.Query);
    }

    [Fact]
    public async Task PrefixesConfiguredBasePath()
    {
        var result = await SendThrough(
            "https://gateway.example.com/maxio",
            "https://cp-exp-8.chargify.com/customers/lookup.json?reference=abc");

        Assert.Equal("gateway.example.com", result.Host);
        Assert.Equal("/maxio/customers/lookup.json", result.AbsolutePath);
        Assert.Equal("?reference=abc", result.Query);
    }
}
