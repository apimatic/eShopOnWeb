using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Customer resolution (UC1's idempotency guarantee) and subscription reads and writes.
/// </summary>
public class MaxioBillingClientSubscriptionTests
{
    private const string UserReference = "demouser@microsoft.com";

    [Fact]
    public async Task FindCustomerByReferenceAsync_ReturnsTheMappedCustomer()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Customer(id: 55001, reference: UserReference));

        var customer = await builder.Build().FindCustomerByReferenceAsync(UserReference);

        Assert.NotNull(customer);
        Assert.Equal(55001, customer!.Id);
        Assert.Equal(UserReference, customer.Reference);
        Assert.Equal(UserReference, customer.Email);
    }

    [Fact]
    public async Task FindCustomerByReferenceAsync_ReturnsNullWhenNoCustomerExistsYet()
    {
        var builder = new BillingClientBuilder()
            .Respond(HttpStatusCode.NotFound, """{"error":"Customer not found"}""");

        var customer = await builder.Build().FindCustomerByReferenceAsync("nobody@example.com");

        Assert.Null(customer);
    }

    [Fact]
    public async Task EnsureCustomerAsync_ReusesAnExistingCustomerWithoutCreatingASecond()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Customer(id: 55001, reference: UserReference));

        var customer = await builder.Build().EnsureCustomerAsync(
            new BillingCustomerRegistration(UserReference, UserReference, "demouser", "eShopOnWeb"));

        Assert.Equal(55001, customer.Id);

        // Exactly one lookup and no create: repeated subscribes must not duplicate the customer.
        var request = Assert.Single(builder.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task EnsureCustomerAsync_CreatesTheCustomerWhenTheReferenceIsUnknown()
    {
        var builder = new BillingClientBuilder()
            .Respond(HttpStatusCode.NotFound, """{"error":"Customer not found"}""")
            .RespondWithJson(MaxioResponses.Customer(id: 55002, reference: UserReference));

        var customer = await builder.Build().EnsureCustomerAsync(
            new BillingCustomerRegistration(UserReference, UserReference, "demouser", "eShopOnWeb"));

        Assert.Equal(55002, customer.Id);
        Assert.Equal(2, builder.Handler.Requests.Count);

        var create = builder.Handler.Requests[1];
        Assert.Equal(HttpMethod.Post, create.Method);
        Assert.Contains(UserReference, create.Body);
        Assert.Contains("\"reference\"", create.Body);
    }

    [Fact]
    public async Task EnsureCustomerAsync_RecoversWhenAConcurrentCallAlreadyCreatedTheCustomer()
    {
        var builder = new BillingClientBuilder()
            // First lookup misses, the create loses the race, and the re-read finds the winner.
            .Respond(HttpStatusCode.NotFound, """{"error":"Customer not found"}""")
            .Respond(HttpStatusCode.UnprocessableEntity, """{"errors":{"reference":["must be unique"]}}""")
            .RespondWithJson(MaxioResponses.Customer(id: 55003, reference: UserReference));

        var customer = await builder.Build().EnsureCustomerAsync(
            new BillingCustomerRegistration(UserReference, UserReference, "demouser", "eShopOnWeb"));

        Assert.Equal(55003, customer.Id);
    }

    [Fact]
    public async Task EnsureCustomerAsync_SurfacesACreateFailureThatIsNotARace()
    {
        var builder = new BillingClientBuilder()
            .Respond(HttpStatusCode.NotFound, """{"error":"Customer not found"}""")
            .Respond(HttpStatusCode.UnprocessableEntity, """{"errors":{"email":["is invalid"]}}""")
            .Respond(HttpStatusCode.NotFound, """{"error":"Customer not found"}""");

        // The customer still does not exist on the re-read, so the failure is genuine and surfaces.
        await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().EnsureCustomerAsync(
                new BillingCustomerRegistration(UserReference, "bad", "demouser", "eShopOnWeb")));
    }

    [Fact]
    public async Task EnsureCustomerAsync_TranslatesAnErrorBodyTheSdkCannotDeserialize()
    {
        var builder = new BillingClientBuilder()
            .Respond(HttpStatusCode.NotFound, """{"error":"Customer not found"}""")
            // A shape the SDK's generated error model does not match. It must not escape as a
            // raw JsonException.
            .Respond(HttpStatusCode.UnprocessableEntity, MaxioResponses.ErrorList("Email: is invalid."))
            .Respond(HttpStatusCode.NotFound, """{"error":"Customer not found"}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().EnsureCustomerAsync(
                new BillingCustomerRegistration(UserReference, "bad", "demouser", "eShopOnWeb")));

        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_MapsTheSubscriptionTheProviderReturns()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Site())
            .RespondWithJson(MaxioResponses.Subscription(
                id: 90001, state: "active", customerId: 55001, customerReference: UserReference,
                productHandle: "eshop-pro", productPriceInCents: 29900));

        var subscription = await builder.Build().CreateSubscriptionAsync(55001, "eshop-pro");

        Assert.Equal(90001, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal(55001, subscription.CustomerId);
        Assert.Equal(UserReference, subscription.CustomerReference);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal(29900L, subscription.PlanPriceInCents);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.True(subscription.IsLive);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), subscription.NextAssessmentAt);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SendsTheCustomerAndPlanToTheProvider()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Site())
            .RespondWithJson(MaxioResponses.Subscription());

        await builder.Build().CreateSubscriptionAsync(55001, "eshop-pro");

        var request = builder.Handler.Requests[1];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("\"customer_id\":55001", request.Body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request.Body);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_BillsByRemittanceOnARelationshipInvoicingSite()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Site(relationshipInvoicingEnabled: true))
            .RespondWithJson(MaxioResponses.Subscription());

        await builder.Build().CreateSubscriptionAsync(55001, "eshop-pro");

        // Without this the site's automatic collection default rejects the plan for having no
        // card on file, even though the plan itself requires no payment method.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", builder.Handler.Requests[1].Body);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_BillsByInvoiceOnALegacyStatementsSite()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Site(relationshipInvoicingEnabled: false))
            .RespondWithJson(MaxioResponses.Subscription());

        await builder.Build().CreateSubscriptionAsync(55001, "eshop-pro");

        Assert.Contains("\"payment_collection_method\":\"invoice\"", builder.Handler.Requests[1].Body);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ReadsTheSiteOnceAndReusesTheCollectionMethod()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Site())
            .RespondWithJson(MaxioResponses.Subscription())
            .RespondWithJson(MaxioResponses.Subscription());

        var client = builder.Build();
        await client.CreateSubscriptionAsync(55001, "eshop-pro");
        await client.CreateSubscriptionAsync(55002, "eshop-pro");

        // One site read plus two subscribes: the stub throws on any unqueued extra request.
        Assert.Equal(3, builder.Handler.Requests.Count);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SurfacesAProviderValidationFailure()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Site())
            .Respond(
                HttpStatusCode.UnprocessableEntity,
                MaxioResponses.ErrorList("Product: payment method is required.", "Credit card: is missing."));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().CreateSubscriptionAsync(55001, "eshop-pro"));

        Assert.Contains("Product: payment method is required.", exception.ProviderErrors);
        Assert.Contains("Credit card: is missing.", exception.ProviderErrors);
        Assert.Contains("payment method is required", exception.DisplayMessage);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ReturnsNullForAnUnknownIdentifier()
    {
        var builder = new BillingClientBuilder()
            .Respond(HttpStatusCode.NotFound, """{"error":"Subscription not found"}""");

        var subscription = await builder.Build().GetSubscriptionAsync(404404);

        Assert.Null(subscription);
    }

    [Theory]
    [InlineData("active", SubscriptionState.Active)]
    [InlineData("trialing", SubscriptionState.Trialing)]
    [InlineData("on_hold", SubscriptionState.Paused)]
    [InlineData("paused", SubscriptionState.Paused)]
    [InlineData("canceled", SubscriptionState.Canceled)]
    [InlineData("past_due", SubscriptionState.PastDue)]
    [InlineData("expired", SubscriptionState.Expired)]
    [InlineData("trial_ended", SubscriptionState.TrialEnded)]
    [InlineData("unpaid", SubscriptionState.Unpaid)]
    [InlineData("failed_to_create", SubscriptionState.Failed)]
    [InlineData("some_future_state", SubscriptionState.Unknown)]
    public async Task GetSubscriptionAsync_NormalizesTheProviderState(string wireState, SubscriptionState expected)
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Subscription(state: wireState));

        var subscription = await builder.Build().GetSubscriptionAsync(90001);

        Assert.NotNull(subscription);
        Assert.Equal(expected, subscription!.State);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ReportsAPendingEndOfPeriodCancellation()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Subscription(cancelAtEndOfPeriod: true));

        var subscription = await builder.Build().GetSubscriptionAsync(90001);

        Assert.True(subscription!.CancelAtEndOfPeriod);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ReportsAScheduledPlanChange()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.Subscription(nextProductHandle: "basic-plan"));

        var subscription = await builder.Build().GetSubscriptionAsync(90001);

        Assert.Equal("basic-plan", subscription!.NextPlanHandle);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerAsync_ReturnsTheNewestSubscriptionFirst()
    {
        var builder = new BillingClientBuilder()
            .RespondWithJson(MaxioResponses.SubscriptionList(
                MaxioResponses.SubscriptionBody(id: 90001, state: "canceled"),
                MaxioResponses.SubscriptionBody(id: 90007, state: "active")));

        var subscriptions = await builder.Build().ListSubscriptionsForCustomerAsync(55001);

        Assert.Collection(
            subscriptions,
            subscription => Assert.Equal(90007, subscription.Id),
            subscription => Assert.Equal(90001, subscription.Id));
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerAsync_ReturnsAnEmptyCollectionForACustomerWithNone()
    {
        var builder = new BillingClientBuilder().RespondWithJson("[]");

        var subscriptions = await builder.Build().ListSubscriptionsForCustomerAsync(55001);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerAsync_SurfacesAProviderOutage()
    {
        var builder = new BillingClientBuilder()
            .Respond(HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().ListSubscriptionsForCustomerAsync(55001));

        Assert.Equal((int)HttpStatusCode.InternalServerError, exception.StatusCode);
    }

    [Fact]
    public async Task AnyOperation_SurfacesATransportFailureAsATypedException()
    {
        // No response is queued, so the stub transport throws — standing in for an unreachable host.
        var builder = new BillingClientBuilder();

        await Assert.ThrowsAnyAsync<Exception>(() => builder.Build().GetSubscriptionAsync(90001));
    }
}
