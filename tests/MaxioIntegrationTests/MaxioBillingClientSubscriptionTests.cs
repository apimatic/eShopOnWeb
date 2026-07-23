using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Customer idempotency, enrolment, and the state mapping the whole feature branches on.
/// </summary>
public class MaxioBillingClientSubscriptionTests
{
    [Fact]
    public async Task FindCustomerAsync_ReturnsTheCustomer_WhenTheReferenceResolves()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.CustomerResponse(MaxioPayloads.Customer()));

        var (client, _) = TestClientFactory.Create(handler);

        var customer = await client.FindCustomerAsync("demouser@microsoft.com");

        Assert.NotNull(customer);
        Assert.Equal(500123, customer!.Id);
        Assert.Equal("demouser@microsoft.com", customer.Reference);
    }

    [Fact]
    public async Task FindCustomerAsync_ReturnsNull_ForAnUnknownReference()
    {
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.NotFound, """{"error":"Customer not found"}""");

        var (client, _) = TestClientFactory.Create(handler);

        Assert.Null(await client.FindCustomerAsync("nobody@example.com"));
    }

    [Fact]
    public async Task EnsureCustomerAsync_ReturnsTheExistingCustomer_WithoutCreatingASecondOne()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.CustomerResponse(MaxioPayloads.Customer()));

        var (client, _) = TestClientFactory.Create(handler);

        var customer = await client.EnsureCustomerAsync(
            BillingCustomerRegistration.ForUser("demouser@microsoft.com"));

        Assert.Equal(500123, customer.Id);

        // Idempotency: exactly one lookup, and no create.
        var only = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, only.Method);
    }

    [Fact]
    public async Task EnsureCustomerAsync_CreatesTheCustomer_WhenTheReferenceIsUnknown()
    {
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.NotFound, """{"error":"not found"}""")
            .EnqueueOk(MaxioPayloads.CustomerResponse(MaxioPayloads.Customer()));

        var (client, _) = TestClientFactory.Create(handler);

        var customer = await client.EnsureCustomerAsync(
            BillingCustomerRegistration.ForUser("demouser@microsoft.com"));

        Assert.Equal(500123, customer.Id);

        var create = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, create.Method);
        Assert.Contains("\"reference\":\"demouser@microsoft.com\"", create.Body, StringComparison.Ordinal);
        Assert.Contains("\"email\":\"demouser@microsoft.com\"", create.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureCustomerAsync_RecoversFromAConcurrentCreate_ByReReadingAfterTheCreateIsRejected()
    {
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.NotFound, """{"error":"not found"}""")
            .Enqueue(HttpStatusCode.UnprocessableEntity,
                MaxioPayloads.CustomerValidationErrors("Reference: must be unique."))
            .EnqueueOk(MaxioPayloads.CustomerResponse(MaxioPayloads.Customer()));

        var (client, _) = TestClientFactory.Create(handler);

        var customer = await client.EnsureCustomerAsync(
            BillingCustomerRegistration.ForUser("demouser@microsoft.com"));

        Assert.Equal(500123, customer.Id);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task EnsureCustomerAsync_SurfacesTheRejectionDetail_WhenTheCustomerStillCannotBeFound()
    {
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.NotFound, """{"error":"not found"}""")
            .Enqueue(HttpStatusCode.UnprocessableEntity,
                MaxioPayloads.CustomerValidationErrors("Email: is invalid."))
            .Enqueue(HttpStatusCode.NotFound, """{"error":"not found"}""");

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.EnsureCustomerAsync(BillingCustomerRegistration.ForUser("bad@example.com")));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Email: is invalid.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARejectionBodyTheSdkCannotParse_StillSurfacesAsATypedBillingFailure()
    {
        // The provider's customer-error payload does not always match the shape the SDK models. A raw
        // deserialization exception must never escape the seam.
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.NotFound, """{"error":"not found"}""")
            .Enqueue(HttpStatusCode.UnprocessableEntity,
                MaxioPayloads.ValidationErrors("Reference: has already been taken."))
            .Enqueue(HttpStatusCode.NotFound, """{"error":"not found"}""");

        var (client, logger) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.EnsureCustomerAsync(BillingCustomerRegistration.ForUser("odd@example.com")));

        Assert.Contains("unexpected response", exception.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(exception.InnerException);
        Assert.Contains(logger.Warnings,
            warning => warning.Contains("could not be interpreted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SendsTheCustomerIdAndThePlanHandle()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.SubscriptionResponse(MaxioPayloads.Subscription()));

        var (client, _) = TestClientFactory.Create(handler);

        await client.CreateSubscriptionAsync(500123, "eshop-pro");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("\"customer_id\":500123", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_BillsByRemittance_BecauseTheStorefrontCapturesNoCardDetails()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.SubscriptionResponse(MaxioPayloads.Subscription()));

        var (client, _) = TestClientFactory.Create(handler);

        await client.CreateSubscriptionAsync(500123, "eshop-pro");

        // Without this the provider refuses to open a subscription carrying an immediate balance, since
        // no payment profile exists to charge.
        Assert.Contains(
            "\"payment_collection_method\":\"remittance\"",
            handler.LastRequest.Body,
            StringComparison.Ordinal);

        // And no card data is ever sent from this application.
        Assert.DoesNotContain("credit_card", handler.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_MapsTheProviderResponseOntoTheDomainSubscription()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.SubscriptionResponse(MaxioPayloads.Subscription()));

        var (client, _) = TestClientFactory.Create(handler);

        var subscription = await client.CreateSubscriptionAsync(500123, "eshop-pro");

        Assert.Equal(60001, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal("active", subscription.ProviderState);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal("demouser@microsoft.com", subscription.CustomerReference);
        Assert.Equal(500123, subscription.CustomerId);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)), subscription.CurrentPeriodEndsAt);
        Assert.True(subscription.IsActive);
        Assert.True(subscription.IsLive);
        Assert.False(subscription.CancelAtEndOfPeriod);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SurfacesTheProvidersValidationMessages()
    {
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.UnprocessableEntity, MaxioPayloads.ValidationErrors(
                "Product: is required.", "Customer: must exist."));

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CreateSubscriptionAsync(500123, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("create subscription", exception.Operation);
        Assert.Contains("Product: is required.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Customer: must exist.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ReturnsNull_ForAnUnknownId()
    {
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.NotFound, """{"error":"Subscription not found"}""");

        var (client, _) = TestClientFactory.Create(handler);

        Assert.Null(await client.GetSubscriptionAsync(123456));
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsEveryCustomerSubscription()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.SubscriptionList(
                MaxioPayloads.Subscription(60001, "active"),
                MaxioPayloads.Subscription(60002, "canceled", canceledAt: "2026-06-01T00:00:00-04:00")));

        var (client, _) = TestClientFactory.Create(handler);

        var subscriptions = await client.ListSubscriptionsAsync(500123);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal(SubscriptionState.Active, subscriptions[0].State);
        Assert.Equal(SubscriptionState.Canceled, subscriptions[1].State);
    }

    [Fact]
    public async Task ListSubscriptionsAsync_ReturnsAnEmptyList_WhenTheCustomerHasNone()
    {
        var handler = new FakeMaxioHandler().EnqueueOk("[]");
        var (client, _) = TestClientFactory.Create(handler);

        Assert.Empty(await client.ListSubscriptionsAsync(500123));
    }

    [Theory]
    [InlineData("active", SubscriptionState.Active, true, true)]
    [InlineData("trialing", SubscriptionState.Trialing, true, true)]
    [InlineData("past_due", SubscriptionState.PastDue, false, true)]
    [InlineData("on_hold", SubscriptionState.Paused, false, true)]
    [InlineData("paused", SubscriptionState.Paused, false, true)]
    [InlineData("canceled", SubscriptionState.Canceled, false, false)]
    [InlineData("expired", SubscriptionState.Expired, false, false)]
    [InlineData("unpaid", SubscriptionState.Unpaid, false, false)]
    [InlineData("trial_ended", SubscriptionState.TrialEnded, false, false)]
    [InlineData("failed_to_create", SubscriptionState.Failed, false, false)]
    public async Task GetSubscriptionAsync_NormalizesEveryProviderState(
        string providerState,
        SubscriptionState expected,
        bool expectedActive,
        bool expectedLive)
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.SubscriptionResponse(MaxioPayloads.Subscription(state: providerState)));

        var (client, _) = TestClientFactory.Create(handler);

        var subscription = await client.GetSubscriptionAsync(60001);

        Assert.NotNull(subscription);
        Assert.Equal(expected, subscription!.State);
        Assert.Equal(expectedActive, subscription.IsActive);
        Assert.Equal(expectedLive, subscription.IsLive);
    }

    [Fact]
    public async Task GetSubscriptionAsync_TreatsAnUnrecognizedStateWithAHoldTimestampAsPaused()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.SubscriptionResponse(
                MaxioPayloads.Subscription(state: "some_future_state", onHoldAt: "2026-07-10T00:00:00-04:00")));

        var (client, _) = TestClientFactory.Create(handler);

        var subscription = await client.GetSubscriptionAsync(60001);

        Assert.Equal(SubscriptionState.Paused, subscription!.State);
        Assert.NotNull(subscription.PausedAt);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ReportsAScheduledPlanChangeAndPendingCancellation()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.SubscriptionResponse(MaxioPayloads.Subscription(
                cancelAtEndOfPeriod: true, nextProductHandle: "basic-plan")));

        var (client, _) = TestClientFactory.Create(handler);

        var subscription = await client.GetSubscriptionAsync(60001);

        Assert.True(subscription!.CancelAtEndOfPeriod);
        Assert.True(subscription.HasScheduledPlanChange);
        Assert.Equal("basic-plan", subscription.ScheduledPlanHandle);
    }
}
