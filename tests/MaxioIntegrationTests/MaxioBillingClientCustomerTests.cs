using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Customer resolution must be idempotent on the eShopOnWeb user reference, so a repeated subscribe
/// never produces a duplicate provider customer.
/// </summary>
public class MaxioBillingClientCustomerTests
{
    private const string Reference = "demouser@microsoft.com";

    private static SubscriberIdentity Subscriber() => new(Reference, Reference);

    [Fact]
    public async Task EnsureCustomerAsync_ReturnsTheExistingCustomer_WithoutCreatingAnother()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.CustomerEnvelope(MaxioJson.Customer())));

        var customer = await BillingClientFixture.Create(handler).EnsureCustomerAsync(Subscriber());

        Assert.Equal(51234, customer.Id);
        Assert.Equal(Reference, customer.Reference);

        // Exactly one call, and it must be the lookup — never a create.
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task EnsureCustomerAsync_LooksUpByBareReference_NotAHandlePrefix()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.Ok(MaxioJson.CustomerEnvelope(MaxioJson.Customer())));

        await BillingClientFixture.Create(handler).EnsureCustomerAsync(Subscriber());

        var request = handler.LastRequest;
        Assert.Contains(Reference, Uri.UnescapeDataString(request.Query));
        Assert.DoesNotContain("handle:", request.Query);
    }

    [Fact]
    public async Task EnsureCustomerAsync_CreatesTheCustomer_KeyedOnTheUserReference()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.NotFound(),
            StubResponse.Created(MaxioJson.CustomerEnvelope(MaxioJson.Customer())));

        var customer = await BillingClientFixture.Create(handler).EnsureCustomerAsync(Subscriber());

        Assert.Equal(51234, customer.Id);
        Assert.Equal(2, handler.Requests.Count);

        var create = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, create.Method);
        Assert.NotNull(create.Body);

        // The reference is what makes the create idempotent, so it must be on the wire.
        Assert.Contains("\"reference\"", create.Body);
        Assert.Contains(Reference, create.Body);
        Assert.Contains("\"email\"", create.Body);
    }

    [Fact]
    public async Task EnsureCustomerAsync_DerivesNames_SoRequiredProviderFieldsAreNeverEmpty()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.NotFound(),
            StubResponse.Created(MaxioJson.CustomerEnvelope(MaxioJson.Customer())));

        await BillingClientFixture.Create(handler).EnsureCustomerAsync(new SubscriberIdentity("demouser@microsoft.com"));

        var body = handler.LastRequest.Body!;
        Assert.Contains("\"first_name\":\"demouser\"", body.Replace(" ", string.Empty));
        Assert.Contains("\"last_name\"", body);
    }

    [Fact]
    public async Task EnsureCustomerAsync_RecoversFromAConcurrentCreate_ByRereadingTheCustomer()
    {
        // Lookup misses, the create loses a race (422), and the re-read finds the winner's customer.
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.NotFound(),
            StubResponse.UnprocessableEntity(MaxioJson.CustomerErrors("reference", "must be unique.")),
            StubResponse.Ok(MaxioJson.CustomerEnvelope(MaxioJson.Customer())));

        var customer = await BillingClientFixture.Create(handler).EnsureCustomerAsync(Subscriber());

        Assert.Equal(51234, customer.Id);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task EnsureCustomerAsync_Throws_WhenTheCreateIsRejectedAndNoCustomerExists()
    {
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.NotFound(),
            StubResponse.UnprocessableEntity(MaxioJson.CustomerErrors("email", "is invalid.")),
            StubResponse.NotFound());

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler).EnsureCustomerAsync(Subscriber()));

        Assert.Equal(422, ex.StatusCode);
    }

    [Fact]
    public async Task EnsureCustomerAsync_SurfacesAnUnreadableErrorBody_AsATypedProviderError()
    {
        // A 422 whose body does not match the schema the SDK expects must still come out as the
        // integration's own exception type, never as a raw JsonException from the SDK.
        var handler = StubHttpMessageHandler.Sequence(
            StubResponse.NotFound(),
            StubResponse.UnprocessableEntity("{ \"errors\": [\"unexpected shape\"] }"),
            StubResponse.NotFound());

        await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler).EnsureCustomerAsync(Subscriber()));
    }

    [Fact]
    public async Task ListPlansAsync_SurfacesAnUnparseableSuccessBody_AsATypedProviderError()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns("{ \"not\": \"a product list\" }");

        await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(handler).ListPlansAsync());
    }

    [Fact]
    public async Task FindCustomerAsync_ReturnsNull_ForAnUnknownReference()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.Errors("Not Found"), HttpStatusCode.NotFound);

        Assert.Null(await BillingClientFixture.Create(handler).FindCustomerAsync("nobody@example.com"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FindCustomerAsync_ReturnsNull_WithoutCallingTheProvider_ForABlankReference(string blank)
    {
        var handler = StubHttpMessageHandler.AlwaysReturns("{}");

        Assert.Null(await BillingClientFixture.Create(handler).FindCustomerAsync(blank));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FindCustomerAsync_MapsTheCustomerShape()
    {
        var handler = StubHttpMessageHandler.AlwaysReturns(MaxioJson.CustomerEnvelope(MaxioJson.Customer()));

        var customer = await BillingClientFixture.Create(handler).FindCustomerAsync(Reference);

        Assert.NotNull(customer);
        Assert.Equal("Demo", customer.FirstName);
        Assert.Equal("User", customer.LastName);
        Assert.Equal(Reference, customer.Email);
        Assert.NotNull(customer.CreatedAt);
    }

    [Fact]
    public void SubscriberIdentity_RejectsABlankReference()
    {
        Assert.ThrowsAny<ArgumentException>(() => new SubscriberIdentity("   "));
    }
}
