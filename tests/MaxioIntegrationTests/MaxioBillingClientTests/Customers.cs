using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class Customers
{
    [Fact]
    public async Task FindCustomerByReferenceMapsTheProviderRecord()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.Customer);
        var client = BillingClientFixture.Create(handler);

        var customer = await client.FindCustomerByReferenceAsync("shopper@example.com");

        Assert.NotNull(customer);
        Assert.Equal(555001, customer!.Id);
        Assert.Equal("shopper@example.com", customer.Reference);
        Assert.Equal("shopper@example.com", customer.Email);
    }

    [Fact]
    public async Task FindCustomerByReferenceReturnsNullForAnUnknownReference()
    {
        var handler = StubHttpMessageHandler.Always(string.Empty, HttpStatusCode.NotFound);
        var client = BillingClientFixture.Create(handler);

        Assert.Null(await client.FindCustomerByReferenceAsync("nobody@example.com"));
    }

    [Fact]
    public async Task FindCustomerByReferenceReturnsNullForAnEmptyBodied4xx()
    {
        // The provider is not consistent about which 4xx an unknown reference produces, so an empty-bodied
        // client error is treated as "no such customer" too.
        var handler = StubHttpMessageHandler.Always(string.Empty, HttpStatusCode.Forbidden);
        var client = BillingClientFixture.Create(handler);

        Assert.Null(await client.FindCustomerByReferenceAsync("nobody@example.com"));
    }

    [Fact]
    public async Task FindCustomerByReferenceStillFailsLoudlyOnAServerError()
    {
        var handler = StubHttpMessageHandler.Always("""{"error":"boom"}""", HttpStatusCode.InternalServerError);
        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.FindCustomerByReferenceAsync("shopper@example.com"));

        Assert.Equal(500, exception.StatusCode);
    }

    [Fact]
    public async Task FindCustomerByReferenceMakesNoProviderCallForAnEmptyReference()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.Customer);
        var client = BillingClientFixture.Create(handler);

        Assert.Null(await client.FindCustomerByReferenceAsync("  "));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateCustomerSendsTheUserReferenceSoRepeatedSubscribesAreIdempotent()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.Customer, HttpStatusCode.Created);
        var client = BillingClientFixture.Create(handler);

        var created = await client.CreateCustomerAsync(new NewBillingCustomer
        {
            Reference = "shopper@example.com",
            Email = "shopper@example.com",
            FirstName = "shopper",
            LastName = "Customer"
        });

        Assert.Equal(555001, created.Id);

        var sent = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Contains("\"reference\":\"shopper@example.com\"", sent.Body.Replace(" ", string.Empty));
        Assert.Contains("\"email\":\"shopper@example.com\"", sent.Body.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task CreateCustomerSurfacesAValidationFailureAsATypedException()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ValidationErrors,
            HttpStatusCode.UnprocessableEntity);
        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CreateCustomerAsync(new NewBillingCustomer
            {
                Reference = "shopper@example.com",
                Email = "not-an-email",
                FirstName = "shopper",
                LastName = "Customer"
            }));

        Assert.Equal(422, exception.StatusCode);
    }
}
