using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Walmart;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.WalmartStockEndpoints;

[TestClass]
public class WalmartBaseUrlHandlerTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (HttpClient Client, CapturingHandler Inner) Build(string? baseUrl)
    {
        var inner = new CapturingHandler();
        var handler = new WalmartBaseUrlHandler(Options.Create(new WalmartSettings
        {
            ClientId = "x",
            ClientSecret = "y",
            BaseUrl = baseUrl,
        }))
        { InnerHandler = inner };
        return (new HttpClient(handler), inner);
    }

    [TestMethod]
    public async Task Override_RewritesSchemeHostPort_PreservingPathAndQuery_OnApiCall()
    {
        var (client, inner) = Build("http://localhost:39200");

        await client.GetAsync("https://marketplace.walmart.com/seller/v1/inventory/v4/items/ESHOP-7?shipNode=1");

        Assert.AreEqual("http", inner.LastUri!.Scheme);
        Assert.AreEqual("localhost", inner.LastUri.Host);
        Assert.AreEqual(39200, inner.LastUri.Port);
        Assert.AreEqual("/seller/v1/inventory/v4/items/ESHOP-7", inner.LastUri.AbsolutePath);
        Assert.AreEqual("?shipNode=1", inner.LastUri.Query);
    }

    [TestMethod]
    public async Task Override_RewritesHost_OnTokenFetch()
    {
        // The token endpoint is an absolute URL the SDK's server options cannot reach — it must be rewritten too.
        var (client, inner) = Build("https://walmart.internal:8443");

        await client.PostAsync("https://marketplace.walmart.com/seller/v1/auth/token", new StringContent(""));

        Assert.AreEqual("https", inner.LastUri!.Scheme);
        Assert.AreEqual("walmart.internal", inner.LastUri.Host);
        Assert.AreEqual(8443, inner.LastUri.Port);
        Assert.AreEqual("/seller/v1/auth/token", inner.LastUri.AbsolutePath);
    }

    [TestMethod]
    public async Task NoOverride_PassesThroughUnchanged()
    {
        var (client, inner) = Build(baseUrl: null);

        await client.GetAsync("https://marketplace.walmart.com/seller/v1/inventory/v4/items");

        Assert.AreEqual("marketplace.walmart.com", inner.LastUri!.Host);
        Assert.AreEqual("/seller/v1/inventory/v4/items", inner.LastUri.AbsolutePath);
    }
}
