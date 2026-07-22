using System.Net;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class UsageTests
{
    [Fact]
    public async Task GetMeteredComponent_MeteredKind_ParsesUnitPriceAndIsMetered()
    {
        const string json = """{ "component": { "id": 3057195, "handle": "api-call", "kind": "metered_component", "unit_price": "0.01" } }""";
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, json);

        var component = await client.GetMeteredComponentAsync();

        Assert.Equal(3057195, component.Id);
        Assert.Equal("api-call", component.Handle);
        Assert.Equal("metered_component", component.Kind);
        Assert.True(component.IsMetered);
        Assert.Equal(0.01m, component.UnitPrice);   // per-unit price magnitude ($0.01)
        Assert.Contains("/components/lookup.json?handle=api-call", handler.Requests[0].PathAndQuery);
    }

    [Fact]
    public async Task GetMeteredComponent_WrongKind_IsMeteredFalse()
    {
        const string json = """{ "component": { "id": 1, "handle": "api-call", "kind": "quantity_based_component", "unit_price": "0.01" } }""";
        var (client, _) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, json);

        var component = await client.GetMeteredComponentAsync();

        Assert.False(component.IsMetered);   // guards UC2 from billing against a mis-seeded component
    }

    [Fact]
    public async Task RecordUsage_PostsQuantityAndMemo_ToMeteredComponentUsagesEndpoint()
    {
        const string json = """{ "usage": { "id": 138522957, "quantity": 5, "memo": "order placed", "component_id": 3057195, "subscription_id": 100 } }""";
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, json);

        var recorded = await client.RecordUsageAsync(100, 5, "order placed");

        Assert.Equal(5, recorded);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/subscriptions/100/components/", request.PathAndQuery);
        Assert.Contains("api-call", request.PathAndQuery);
        Assert.Contains("usages.json", request.PathAndQuery);
        Assert.Contains("\"quantity\":5", request.Body);
        Assert.Contains("\"memo\":\"order placed\"", request.Body);
    }

    [Fact]
    public async Task GetUsageBalance_ReadsUnitBalance()
    {
        const string json = """{ "component": { "component_id": 3057195, "subscription_id": 100, "kind": "metered_component", "unit_balance": 42 } }""";
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, json);

        var balance = await client.GetUsageBalanceAsync(100);

        Assert.Equal(42m, balance);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Contains("/subscriptions/100/components/", handler.Requests[0].PathAndQuery);
    }

    [Fact]
    public async Task GetUsageBalance_NoBalanceField_ReturnsNull()
    {
        const string json = """{ "component": { "component_id": 3057195, "subscription_id": 100, "kind": "metered_component" } }""";
        var (client, _) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, json);

        Assert.Null(await client.GetUsageBalanceAsync(100));
    }
}
