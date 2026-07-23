using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class Customers
{
    private const string Reference = "demouser@microsoft.com";
    private const string LookupPath = "/customers/lookup.json";
    private const string CustomersPath = "/customers.json";

    private readonly RecordingHttpMessageHandler _handler = new();

    [Fact]
    public async Task FindsAnExistingCustomerByReference()
    {
        _handler.RespondJson(HttpMethod.Get, LookupPath, MaxioResponses.Customer);

        var customer = await TestBillingClientFactory.Create(_handler).FindCustomerByReferenceAsync(Reference);

        Assert.NotNull(customer);
        Assert.Equal(MaxioResponses.CustomerId, customer.Id);
        Assert.Equal(Reference, customer.Reference);
        Assert.Equal(Reference, customer.Email);
    }

    /// <summary>Maxio answers 404 when no customer carries the reference; that is "not found", not a failure.</summary>
    [Fact]
    public async Task ReturnsNullWhenNoCustomerCarriesTheReference()
    {
        _handler.RespondStatus(HttpMethod.Get, LookupPath, HttpStatusCode.NotFound);

        var customer = await TestBillingClientFactory.Create(_handler).FindCustomerByReferenceAsync("nobody@example.com");

        Assert.Null(customer);
    }

    [Fact]
    public async Task SendsTheReferenceUrlEncodedAsAQueryParameter()
    {
        _handler.RespondStatus(HttpMethod.Get, LookupPath, HttpStatusCode.NotFound);

        await TestBillingClientFactory.Create(_handler).FindCustomerByReferenceAsync("a b+c@example.com");

        var query = Assert.Single(_handler.Requests).Query;
        Assert.Contains("reference=a%20b%2Bc%40example.com", query);
    }

    /// <summary>
    /// Subscribing repeatedly must never create a duplicate customer, so an existing record is
    /// reused and no creation call is issued.
    /// </summary>
    [Fact]
    public async Task ReusesAnExistingCustomerWithoutCreatingASecondOne()
    {
        _handler.RespondJson(HttpMethod.Get, LookupPath, MaxioResponses.Customer);

        var customer = await TestBillingClientFactory.Create(_handler)
            .EnsureCustomerAsync(new EnsureCustomerRequest(Reference, Reference));

        Assert.Equal(MaxioResponses.CustomerId, customer.Id);
        Assert.Empty(_handler.RequestsFor(HttpMethod.Post, CustomersPath));
    }

    [Fact]
    public async Task CreatesTheCustomerWhenTheReferenceIsUnknown()
    {
        _handler.RespondStatus(HttpMethod.Get, LookupPath, HttpStatusCode.NotFound)
                .RespondJson(HttpMethod.Post, CustomersPath, MaxioResponses.Customer, HttpStatusCode.Created);

        var customer = await TestBillingClientFactory.Create(_handler)
            .EnsureCustomerAsync(new EnsureCustomerRequest(Reference, Reference));

        Assert.Equal(MaxioResponses.CustomerId, customer.Id);

        var body = Assert.Single(_handler.RequestsFor(HttpMethod.Post, CustomersPath)).Body;
        Assert.NotNull(body);
        Assert.Contains($"\"reference\":\"{Reference}\"", body);
        Assert.Contains($"\"email\":\"{Reference}\"", body);
    }

    /// <summary>Maxio requires a name, so a nameless request must still produce a named record.</summary>
    [Fact]
    public async Task SuppliesAFallbackNameWhenNoneIsGiven()
    {
        _handler.RespondStatus(HttpMethod.Get, LookupPath, HttpStatusCode.NotFound)
                .RespondJson(HttpMethod.Post, CustomersPath, MaxioResponses.Customer, HttpStatusCode.Created);

        await TestBillingClientFactory.Create(_handler)
            .EnsureCustomerAsync(new EnsureCustomerRequest(Reference, Reference));

        var body = Assert.Single(_handler.RequestsFor(HttpMethod.Post, CustomersPath)).Body!;
        Assert.Contains($"\"first_name\":\"{Reference}\"", body);
        Assert.Contains("\"last_name\":\"eShopOnWeb\"", body);
    }

    [Fact]
    public async Task UsesTheSuppliedNameWhenOneIsGiven()
    {
        _handler.RespondStatus(HttpMethod.Get, LookupPath, HttpStatusCode.NotFound)
                .RespondJson(HttpMethod.Post, CustomersPath, MaxioResponses.Customer, HttpStatusCode.Created);

        await TestBillingClientFactory.Create(_handler)
            .EnsureCustomerAsync(new EnsureCustomerRequest(Reference, Reference, "Ada", "Lovelace"));

        var body = Assert.Single(_handler.RequestsFor(HttpMethod.Post, CustomersPath)).Body!;
        Assert.Contains("\"first_name\":\"Ada\"", body);
        Assert.Contains("\"last_name\":\"Lovelace\"", body);
    }

    [Fact]
    public void RejectsABlankCustomerReferenceBeforeAnyProviderCall()
    {
        Assert.ThrowsAny<ArgumentException>(() => new EnsureCustomerRequest("  ", "someone@example.com"));
    }
}
