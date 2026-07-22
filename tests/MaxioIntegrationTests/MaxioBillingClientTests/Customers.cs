using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class Customers
{
    private const string UserReference = "demouser@microsoft.com";

    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task FindsAnExistingCustomerByUserReference()
    {
        _handler.RespondOk(HttpMethod.Get, "/customers/lookup.json",
            MaxioJson.Customer(33, UserReference, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        var customer = await client.FindCustomerByReferenceAsync(UserReference);

        Assert.NotNull(customer);
        Assert.Equal(33, customer.Id);
        Assert.Equal(UserReference, customer.Reference);
        Assert.Contains($"reference={Uri.EscapeDataString(UserReference)}", _handler.LastRequest.Query);
    }

    [Fact]
    public async Task ReturnsNullForAnUnknownUserReference()
    {
        // A missing customer is a normal state on the subscribe path, not a failure.
        _handler.Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, MaxioJson.NotFound());
        var client = BillingClientBuilder.Build(_handler);

        Assert.Null(await client.FindCustomerByReferenceAsync("nobody@example.com"));
    }

    [Fact]
    public async Task EnsureCustomerReturnsTheExistingCustomerWithoutCreatingASecond()
    {
        _handler.RespondOk(HttpMethod.Get, "/customers/lookup.json",
            MaxioJson.Customer(33, UserReference, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        var customer = await client.EnsureCustomerAsync(UserReference, UserReference);

        Assert.Equal(33, customer.Id);
        Assert.Empty(_handler.Requests.Where(request => request.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task EnsureCustomerCreatesTheCustomerWhenNoneExists()
    {
        _handler
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, MaxioJson.NotFound())
            .RespondOk(HttpMethod.Post, "/customers.json", MaxioJson.Customer(44, UserReference, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        var customer = await client.EnsureCustomerAsync(UserReference, UserReference);

        Assert.Equal(44, customer.Id);
        Assert.Equal(UserReference, customer.Reference);
    }

    [Fact]
    public async Task EnsureCustomerSendsTheUserReferenceAsTheIdempotencyKey()
    {
        _handler
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, MaxioJson.NotFound())
            .RespondOk(HttpMethod.Post, "/customers.json", MaxioJson.Customer(44, UserReference, UserReference));
        var client = BillingClientBuilder.Build(_handler);

        await client.EnsureCustomerAsync(UserReference, UserReference);

        var created = _handler.RequestsFor("/customers.json").Single(request => request.Method == HttpMethod.Post);
        Assert.Contains("\"reference\"", created.Body);
        Assert.Contains(UserReference, created.Body);
    }

    [Fact]
    public async Task EnsureCustomerDerivesTheRequiredNameDeterministicallyFromTheEmail()
    {
        _handler
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, MaxioJson.NotFound())
            .RespondOk(HttpMethod.Post, "/customers.json", MaxioJson.Customer(44, "ada.lovelace@example.com", "ada.lovelace@example.com"));
        var client = BillingClientBuilder.Build(_handler);

        await client.EnsureCustomerAsync("ada.lovelace@example.com", "ada.lovelace@example.com");

        // The provider requires a first and last name; eShopOnWeb identities are just an email.
        var created = _handler.RequestsFor("/customers.json").Single(request => request.Method == HttpMethod.Post);
        Assert.Contains("\"first_name\":\"Ada\"", created.Body);
        Assert.Contains("\"last_name\":\"Lovelace\"", created.Body);
    }

    [Fact]
    public async Task EnsureCustomerRecoversFromALostCreateRaceByRereadingTheReference()
    {
        // A concurrent subscribe created the customer between our lookup and our create, so the
        // provider rejects the duplicate reference. The customer's subscribe must still succeed —
        // and must do so whatever shape the provider's rejection body happens to take, because
        // idempotency is anchored on the reference, not on parsing an error payload.
        _handler
            .RespondInSequence(HttpMethod.Get, "/customers/lookup.json",
                (HttpStatusCode.NotFound, MaxioJson.NotFound()),
                (HttpStatusCode.OK, MaxioJson.Customer(55, UserReference, UserReference)))
            .Respond(HttpMethod.Post, "/customers.json", HttpStatusCode.UnprocessableEntity,
                MaxioJson.ErrorList("Reference: has already been taken."));
        var client = BillingClientBuilder.Build(_handler);

        var customer = await client.EnsureCustomerAsync(UserReference, UserReference);

        Assert.Equal(55, customer.Id);
    }

    [Fact]
    public async Task EnsureCustomerRecoversFromALostCreateRaceWithTheProvidersOwnErrorShape()
    {
        _handler
            .RespondInSequence(HttpMethod.Get, "/customers/lookup.json",
                (HttpStatusCode.NotFound, MaxioJson.NotFound()),
                (HttpStatusCode.OK, MaxioJson.Customer(55, UserReference, UserReference)))
            .Respond(HttpMethod.Post, "/customers.json", HttpStatusCode.UnprocessableEntity,
                MaxioJson.CustomerErrors("Reference: has already been taken."));
        var client = BillingClientBuilder.Build(_handler);

        var customer = await client.EnsureCustomerAsync(UserReference, UserReference);

        Assert.Equal(55, customer.Id);
    }

    [Fact]
    public async Task EnsureCustomerStillFailsWhenTheCustomerTrulyCannotBeCreated()
    {
        // The create was refused and no customer exists afterwards, so there is nothing to recover
        // to — the caller must see the failure rather than a silent success.
        _handler
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, MaxioJson.NotFound())
            .Respond(HttpMethod.Post, "/customers.json", HttpStatusCode.UnprocessableEntity,
                MaxioJson.CustomerErrors("Email: is invalid."));
        var client = BillingClientBuilder.Build(_handler);

        await Assert.ThrowsAsync<BillingProviderException>(
            () => client.EnsureCustomerAsync(UserReference, UserReference));
    }

    [Fact]
    public async Task EnsureCustomerSurfacesAProviderRefusalAsATypedException()
    {
        _handler
            .Respond(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, MaxioJson.NotFound())
            .Unreachable(HttpMethod.Post, "/customers.json");
        var client = BillingClientBuilder.Build(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.EnsureCustomerAsync(UserReference, UserReference));

        Assert.True(exception.IsTransport);
    }
}
