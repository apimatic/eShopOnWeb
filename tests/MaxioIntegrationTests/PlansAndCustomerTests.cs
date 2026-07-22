using System.Linq;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class PlansAndCustomerTests
{
    private const string TwoPlansJson = """
    [
      { "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "description": "Pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false } },
      { "product": { "id": 7126958, "name": "Basic Plan", "handle": "basic-plan", "description": "Basic", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "require_credit_card": false } }
    ]
    """;

    [Fact]
    public async Task ListPlans_MapsHandlesAndConvertsCentsToDollars()
    {
        var (client, _) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, TwoPlansJson);

        var plans = (await client.ListPlansAsync()).ToList();

        Assert.Equal(2, plans.Count);

        var pro = plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(299.00m, pro.Price);          // 29900 cents => $299.00 (magnitude correctness)
        Assert.Equal("month", pro.Interval);
        Assert.Equal(1, pro.IntervalCount);
        Assert.False(pro.RequiresPaymentMethod);

        var basic = plans.Single(p => p.Handle == "basic-plan");
        Assert.Equal(29.00m, basic.Price);          // 2900 cents => $29.00
    }

    [Fact]
    public async Task ListPlans_EmptyFamily_ReturnsEmpty()
    {
        var (client, _) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, "[]");

        var plans = await client.ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task ListPlans_TargetsConfiguredBaseUrl_FamilyByHandle_WithBasicAuth()
    {
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, TwoPlansJson);

        await client.ListPlansAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://billing.test.local", $"{request.Uri.Scheme}://{request.Uri.Authority}");
        // Resolves the family by its durable handle (ids can be reassigned on re-seed, §1.3).
        Assert.Contains("product_families/", request.PathAndQuery);
        Assert.Contains("eshop-subscribe/products.json", request.PathAndQuery);
        Assert.Contains("handle", request.PathAndQuery);
        Assert.Equal("Basic", request.AuthScheme);       // Maxio HTTP Basic auth is applied
        Assert.False(string.IsNullOrEmpty(request.AuthParameter));
    }

    [Fact]
    public async Task ListPlans_ProviderUnreachable_ThrowsBillingProviderException()
    {
        var (client, _) = MaxioClientHarness.Create(StubHttpMessageHandler.NetworkFailure());

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());
        Assert.Contains("Could not reach", ex.Message);
    }

    [Fact]
    public async Task FindCustomerByReference_WhenFound_MapsCustomer()
    {
        const string json = """{ "customer": { "id": 14714298, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" } }""";
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, json);

        var customer = await client.FindCustomerByReferenceAsync("demouser@microsoft.com");

        Assert.NotNull(customer);
        Assert.Equal(14714298, customer!.Id);
        Assert.Equal("demouser@microsoft.com", customer.Reference);
        Assert.Contains("/customers/lookup.json?reference=demouser%40microsoft.com", handler.Requests[0].PathAndQuery);
    }

    [Fact]
    public async Task FindCustomerByReference_WhenNotFound_ReturnsNull()
    {
        var (client, _) = MaxioClientHarness.WithResponse(HttpStatusCode.NotFound, """{ "error": "not found" }""");

        var customer = await client.FindCustomerByReferenceAsync("nobody@example.com");

        Assert.Null(customer);   // 404 on lookup is "not found", never an exception
    }

    [Fact]
    public async Task CreateCustomer_PostsReferenceAndEmail_MapsResult()
    {
        const string json = """{ "customer": { "id": 555, "reference": "demouser@microsoft.com", "email": "demouser@microsoft.com" } }""";
        var (client, handler) = MaxioClientHarness.WithResponse(HttpStatusCode.OK, json);

        var customer = await client.CreateCustomerAsync("demouser@microsoft.com", "demouser@microsoft.com");

        Assert.Equal(555, customer.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/customers.json", request.PathAndQuery);
        Assert.Contains("\"reference\":\"demouser@microsoft.com\"", request.Body);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", request.Body);
    }
}
