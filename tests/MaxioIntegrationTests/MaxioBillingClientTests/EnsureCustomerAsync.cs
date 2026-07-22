using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class EnsureCustomerAsync
{
    private const string Reference = "demouser@microsoft.com";

    private readonly StubHttpMessageHandler _handler = new();

    [Fact]
    public async Task ReusesAnExistingCustomerWithoutCreatingASecondOne()
    {
        _handler.RespondWithJson(ProviderPayloads.CustomerResponse(ProviderPayloads.Customer));

        var customer = await BillingClientFixture.Create(_handler)
            .EnsureCustomerAsync(Reference, Reference, "demouser", "microsoft");

        Assert.Equal(5551212, customer.Id);
        Assert.Equal(Reference, customer.Reference);

        // Exactly one request — the lookup. Nothing was created.
        Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, _handler.Requests[0].Method);
    }

    [Fact]
    public async Task CreatesTheCustomerKeyedOnTheEShopUserWhenTheLookupFindsNothing()
    {
        _handler.RespondWithError(HttpStatusCode.NotFound);
        _handler.RespondWithJson(ProviderPayloads.CustomerResponse(ProviderPayloads.Customer));

        var customer = await BillingClientFixture.Create(_handler)
            .EnsureCustomerAsync(Reference, Reference, "demouser", "microsoft");

        Assert.Equal(5551212, customer.Id);

        var create = _handler.LastRequest;
        Assert.Equal(HttpMethod.Post, create.Method);
        // The reference is what makes a repeated subscribe idempotent, so it must be sent.
        Assert.Contains("\"reference\":\"demouser@microsoft.com\"", create.Body);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", create.Body);
    }

    [Fact]
    public async Task TreatsASuccessfulLookupCarryingNoCustomerIdAsNotFound()
    {
        _handler.RespondWithJson("""{"customer": {"id": null, "reference": null}}""");
        _handler.RespondWithJson(ProviderPayloads.CustomerResponse(ProviderPayloads.Customer));

        var customer = await BillingClientFixture.Create(_handler)
            .EnsureCustomerAsync(Reference, Reference, "demouser", "microsoft");

        Assert.Equal(5551212, customer.Id);
        Assert.Equal(HttpMethod.Post, _handler.LastRequest.Method);
    }

    [Fact]
    public async Task ReturnsTheRacedCustomerWhenAConcurrentCallCreatedItFirst()
    {
        _handler.RespondWithError(HttpStatusCode.NotFound);                       // initial lookup
        _handler.RespondWithError(HttpStatusCode.UnprocessableEntity,
            """{"errors": {"per_page": ["Reference: must be unique."]}}""");      // create loses the race
        _handler.RespondWithJson(ProviderPayloads.CustomerResponse(ProviderPayloads.Customer)); // re-lookup

        var customer = await BillingClientFixture.Create(_handler)
            .EnsureCustomerAsync(Reference, Reference, "demouser", "microsoft");

        Assert.Equal(5551212, customer.Id);
    }

    [Fact]
    public async Task SurfacesAGenuineRejectionAsATypedBillingFailure()
    {
        _handler.RespondWithError(HttpStatusCode.NotFound);
        _handler.RespondWithError(HttpStatusCode.UnprocessableEntity,
            """{"errors": {"per_page": ["Email: is invalid."]}}""");
        _handler.AlwaysRespondWithError(HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(_handler)
                .EnsureCustomerAsync(Reference, "nonsense", "demouser", "microsoft"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Email: is invalid.", exception.ProviderMessage);
    }

    [Fact]
    public async Task StillReportsATypedFailureWhenTheErrorBodyIsNotTheShapeTheSdkExpects()
    {
        _handler.RespondWithError(HttpStatusCode.NotFound);
        // The provider's customer-validation payload is not guaranteed to match the generated model,
        // so an unreadable error body must not escape as a raw deserialization exception.
        _handler.RespondWithError(HttpStatusCode.UnprocessableEntity,
            """{"errors": ["Email: is invalid."]}""");
        _handler.AlwaysRespondWithError(HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(_handler)
                .EnsureCustomerAsync(Reference, "nonsense", "demouser", "microsoft"));

        Assert.Equal(0, exception.StatusCode);
    }

    [Fact]
    public async Task RefusesToCreateACustomerWithoutAStableReference()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => BillingClientFixture.Create(_handler).EnsureCustomerAsync("", "a@b.c", "a", "b"));

        Assert.Empty(_handler.Requests);
    }
}
