using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services.MaxioBillingClientTests;

public class GetMeteredUsageComponentAsync
{
    private static MaxioSettings Settings(string? baseUrl = "http://mock.local") => new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        Environment = "US",
        BaseUrl = baseUrl,
        ProductFamilyId = 3008866,
        ProductFamilyHandle = "eshop-subscribe",
        MeteredComponentHandle = "api-call",
        MeteredComponentId = 3033795
    };

    [Fact]
    public async Task ReturnsComponent_WhenKindIsMetered()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{ "component": { "id": 3033795, "handle": "api-call", "name": "API Calls", "kind": "metered_component" } }""");
        var client = new MaxioBillingClient(new HttpClient(handler), Options.Create(Settings()));

        var component = await client.GetMeteredUsageComponentAsync();

        Assert.Equal(3033795, component.Id);
        Assert.Equal("api-call", component.Handle);
        Assert.True(component.IsMeteredKind);
    }

    [Fact]
    public async Task Throws_WhenComponentIsNotMeteredKind()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{ "component": { "id": 3033795, "handle": "api-call", "name": "API Calls", "kind": "quantity_based_component" } }""");
        var client = new MaxioBillingClient(new HttpClient(handler), Options.Create(Settings()));

        await Assert.ThrowsAsync<BillingProviderException>(() => client.GetMeteredUsageComponentAsync());
    }

    [Fact]
    public async Task RoutesToExplicitBaseUrlOverride_NotSubdomainDerivedHost()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{ "component": { "id": 3033795, "handle": "api-call", "name": "API Calls", "kind": "metered_component" } }""");
        var client = new MaxioBillingClient(new HttpClient(handler), Options.Create(Settings(baseUrl: "http://localhost:8080")));

        await client.GetMeteredUsageComponentAsync();

        Assert.Equal("localhost", handler.LastRequest!.RequestUri!.Host);
        Assert.Equal(8080, handler.LastRequest!.RequestUri!.Port);
    }

    [Fact]
    public async Task RoutesToSubdomainDerivedHost_WhenBaseUrlNotSet()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK,
            """{ "component": { "id": 3033795, "handle": "api-call", "name": "API Calls", "kind": "metered_component" } }""");
        var client = new MaxioBillingClient(new HttpClient(handler), Options.Create(Settings(baseUrl: null)));

        await client.GetMeteredUsageComponentAsync();

        Assert.Equal("acme.chargify.com", handler.LastRequest!.RequestUri!.Host);
    }
}
