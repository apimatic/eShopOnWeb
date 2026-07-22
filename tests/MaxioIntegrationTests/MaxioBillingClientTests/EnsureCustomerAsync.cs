using System.Text.Json;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class EnsureCustomerAsync
{
    [Fact]
    public async Task ReusesTheExistingCustomerAndCreatesNoSecondRecord()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("customers/lookup.json",
            MaxioJson.CustomerResponse(55, "demo@microsoft.com", "demo@microsoft.com"));

        var customer = await builder.Build()
            .EnsureCustomerAsync("demo@microsoft.com", "demo@microsoft.com", null, null);

        Assert.Equal(55, customer.ProviderCustomerId);
        Assert.Equal("demo@microsoft.com", customer.Reference);

        // Idempotency is the whole point: a repeated subscribe must not POST a new customer.
        var request = Assert.Single(builder.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task LooksTheCustomerUpByTheEShopUserReference()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler.RespondWithOk("customers/lookup.json",
            MaxioJson.CustomerResponse(55, "demo@microsoft.com", "demo@microsoft.com"));

        await builder.Build().EnsureCustomerAsync("demo@microsoft.com", "demo@microsoft.com", null, null);

        Assert.Contains("customers/lookup.json?reference=demo%40microsoft.com",
            builder.Handler.LastRequest.Uri.ToString());
    }

    [Fact]
    public async Task CreatesTheCustomerWhenTheReferenceDoesNotResolveYet()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler
            .RespondWithNotFound("customers/lookup.json")
            .RespondWithOk("customers.json",
                MaxioJson.CustomerResponse(77, "new@microsoft.com", "new@microsoft.com"));

        var customer = await builder.Build()
            .EnsureCustomerAsync("new@microsoft.com", "new@microsoft.com", null, null);

        Assert.Equal(77, customer.ProviderCustomerId);
        Assert.Equal(2, builder.Handler.Requests.Count);

        var create = builder.Handler.LastRequest;
        Assert.Equal(HttpMethod.Post, create.Method);

        using var body = JsonDocument.Parse(create.Body!);
        var payload = body.RootElement.GetProperty("customer");
        Assert.Equal("new@microsoft.com", payload.GetProperty("reference").GetString());
        Assert.Equal("new@microsoft.com", payload.GetProperty("email").GetString());
    }

    [Fact]
    public async Task SendsTheSuppliedNameWhenOneIsKnown()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler
            .RespondWithNotFound("customers/lookup.json")
            .RespondWithOk("customers.json",
                MaxioJson.CustomerResponse(78, "ada@microsoft.com", "ada@microsoft.com"));

        await builder.Build().EnsureCustomerAsync("ada@microsoft.com", "ada@microsoft.com", "Ada", "Lovelace");

        using var body = JsonDocument.Parse(builder.Handler.LastRequest.Body!);
        var payload = body.RootElement.GetProperty("customer");
        Assert.Equal("Ada", payload.GetProperty("first_name").GetString());
        Assert.Equal("Lovelace", payload.GetProperty("last_name").GetString());
    }

    [Fact]
    public async Task FallsBackToTheEmailForTheNameBecauseMaxioRequiresOne()
    {
        var builder = new MaxioClientBuilder();
        builder.Handler
            .RespondWithNotFound("customers/lookup.json")
            .RespondWithOk("customers.json",
                MaxioJson.CustomerResponse(79, "demo@microsoft.com", "demo@microsoft.com"));

        await builder.Build().EnsureCustomerAsync("demo@microsoft.com", "demo@microsoft.com", null, null);

        using var body = JsonDocument.Parse(builder.Handler.LastRequest.Body!);
        var payload = body.RootElement.GetProperty("customer");
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("first_name").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("last_name").GetString()));
    }
}
