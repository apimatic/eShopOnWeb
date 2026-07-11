using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services;

/// <summary>
/// Verifies plan.md §2.3's hard requirement: MaxioBillingClient must honor an explicit Maxio:BaseUrl
/// verbatim, and otherwise derive the host from Subdomain - never silently fall back to a hardcoded host.
/// </summary>
public class MaxioBillingClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task ExplicitBaseUrlWinsOverTheSubdomainDerivedHost()
    {
        var handler = new StubHandler();
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "should-be-ignored",
            Environment = "US",
            BaseUrl = "http://localhost:8080",
            ProductFamilyHandle = "eshop-subscribe",
            ProductFamilyId = 3008866,
        };
        var client = new MaxioBillingClient(new HttpClient(handler), Options.Create(settings));

        await client.ListPlansAsync();

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("localhost", handler.LastRequest!.RequestUri!.Host);
        Assert.Equal(8080, handler.LastRequest.RequestUri.Port);
        Assert.Contains("/product_families/3008866/products.json", handler.LastRequest.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task DerivesTheHostFromSubdomainWhenNoBaseUrlIsConfigured()
    {
        var handler = new StubHandler();
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "apimatic-hackathon",
            Environment = "US",
            BaseUrl = null,
            ProductFamilyHandle = "eshop-subscribe",
            ProductFamilyId = 3008866,
        };
        var client = new MaxioBillingClient(new HttpClient(handler), Options.Create(settings));

        await client.ListPlansAsync();

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("apimatic-hackathon.chargify.com", handler.LastRequest!.RequestUri!.Host);
    }
}
