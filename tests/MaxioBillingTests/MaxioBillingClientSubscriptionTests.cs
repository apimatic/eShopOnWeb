using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// Subscription reads and writes: state mapping, period dates, price magnitude, unknown ids, and the
/// guarantee that an unrecognised provider state can never look actionable.
/// </summary>
public class MaxioBillingClientSubscriptionTests
{
    private static BillingCustomer Customer() => new(
        MaxioPayloads.CustomerId, MaxioPayloads.CustomerReference, MaxioPayloads.CustomerReference, "Demo", "User");

    [Fact]
    public async Task CreateSubscriptionAsync_MapsTheSubscription()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription());

        var subscription = await context.Client.CreateSubscriptionAsync(Customer(), "eshop-pro");

        Assert.Equal(MaxioPayloads.SubscriptionId, subscription.Id);
        Assert.Equal(MaxioPayloads.CustomerId, subscription.CustomerId);
        Assert.Equal(MaxioPayloads.CustomerReference, subscription.CustomerReference);
        Assert.Equal("eshop-pro", subscription.PlanHandle);
        Assert.Equal("Pro Plan", subscription.PlanName);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal("active", subscription.ProviderState);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)), subscription.CurrentPeriodEnd);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)), subscription.NextAssessmentAt);
        Assert.Null(subscription.CancellationScheduledAt);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SendsThePlanHandleAndCustomerId()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription());

        await context.Client.CreateSubscriptionAsync(Customer(), "  eshop-pro  ");

        var request = Assert.Single(context.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", request.Body!);
        Assert.Contains("\"customer_id\":" + MaxioPayloads.CustomerId, request.Body!);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_EnrollsWithoutAPaymentMethodByDefault()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription());

        await context.Client.CreateSubscriptionAsync(Customer(), "eshop-pro");

        var request = Assert.Single(context.Handler.Requests);

        // The demo captures no card, so the enrollment must ask Maxio to invoice rather than auto-charge —
        // otherwise Maxio refuses with "No payment method was on file".
        Assert.Contains("\"payment_collection_method\":\"remittance\"", request.Body!);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_HonoursAConfiguredCollectionMethod()
    {
        var settings = BillingTestContext.DefaultSettings();
        settings.PaymentCollectionMethod = "Automatic";

        using var context = new BillingTestContext(settings);
        context.Handler.Enqueue(MaxioPayloads.Subscription());

        await context.Client.CreateSubscriptionAsync(Customer(), "eshop-pro");

        var request = Assert.Single(context.Handler.Requests);
        Assert.Contains("\"payment_collection_method\":\"automatic\"", request.Body!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    public async Task CreateSubscriptionAsync_FallsBackToTheCardFreeMethod_ForAnUnrecognisedSetting(string? configured)
    {
        var settings = BillingTestContext.DefaultSettings();
        settings.PaymentCollectionMethod = configured!;

        using var context = new BillingTestContext(settings);
        context.Handler.Enqueue(MaxioPayloads.Subscription());

        await context.Client.CreateSubscriptionAsync(Customer(), "eshop-pro");

        var request = Assert.Single(context.Handler.Requests);

        // A typo must not silently turn on automatic collection and start demanding cards.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", request.Body!);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_RejectsABlankPlanHandle_WithoutCallingMaxio()
    {
        using var context = new BillingTestContext();

        await Assert.ThrowsAsync<ArgumentException>(
            () => context.Client.CreateSubscriptionAsync(Customer(), "   "));

        Assert.Empty(context.Handler.Requests);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SurfacesProviderRejection()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.ErrorList, HttpStatusCode.UnprocessableEntity);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.CreateSubscriptionAsync(Customer(), "eshop-pro"));

        // The provider's own explanation must reach the caller, not be swallowed.
        Assert.Contains("Payment method is required", exception.Message + exception.ProviderMessage);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ReturnsNull_ForAnUnknownId()
    {
        using var context = new BillingTestContext();
        context.Handler.EnqueueStatus(HttpStatusCode.NotFound);

        Assert.Null(await context.Client.GetSubscriptionAsync(4242));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    public async Task GetSubscriptionAsync_ReturnsNull_ForANonPositiveId_WithoutCallingMaxio(int subscriptionId)
    {
        using var context = new BillingTestContext();

        Assert.Null(await context.Client.GetSubscriptionAsync(subscriptionId));
        Assert.Empty(context.Handler.Requests);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerAsync_ReturnsEmpty_WhenTheCustomerHasNone()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.EmptyList);

        Assert.Empty(await context.Client.ListSubscriptionsForCustomerAsync(MaxioPayloads.CustomerId));
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerAsync_MapsEachSubscription()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.SubscriptionList());

        var subscriptions = await context.Client.ListSubscriptionsForCustomerAsync(MaxioPayloads.CustomerId);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(MaxioPayloads.SubscriptionId, subscription.Id);
        Assert.Equal(SubscriptionState.Active, subscription.State);
        Assert.Equal(299.00m, subscription.PlanPrice);
    }

    [Theory]
    [InlineData("active", nameof(SubscriptionState.Active))]
    [InlineData("trialing", nameof(SubscriptionState.Trialing))]
    [InlineData("assessing", nameof(SubscriptionState.Active))]
    [InlineData("past_due", nameof(SubscriptionState.PastDue))]
    [InlineData("soft_failure", nameof(SubscriptionState.PastDue))]
    [InlineData("trial_ended", nameof(SubscriptionState.PastDue))]
    [InlineData("suspended", nameof(SubscriptionState.Suspended))]
    [InlineData("unpaid", nameof(SubscriptionState.Suspended))]
    [InlineData("on_hold", nameof(SubscriptionState.Paused))]
    [InlineData("paused", nameof(SubscriptionState.Paused))]
    [InlineData("canceled", nameof(SubscriptionState.Cancelled))]
    [InlineData("expired", nameof(SubscriptionState.Expired))]
    [InlineData("pending", nameof(SubscriptionState.Pending))]
    [InlineData("awaiting_signup", nameof(SubscriptionState.Pending))]
    [InlineData("failed_to_create", nameof(SubscriptionState.Failed))]
    public async Task GetSubscriptionAsync_MapsEveryProviderState(string providerState, string expected)
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription(providerState));

        var subscription = await context.Client.GetSubscriptionAsync(MaxioPayloads.SubscriptionId);

        Assert.NotNull(subscription);
        Assert.Equal(expected, subscription!.State.ToString());
        Assert.Equal(providerState, subscription.ProviderState);
    }

    [Fact]
    public async Task GetSubscriptionAsync_MapsAnUnrecognisedStateToUnknown_AndAllowsNoAction()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription("some_future_state"));

        var subscription = await context.Client.GetSubscriptionAsync(MaxioPayloads.SubscriptionId);

        Assert.NotNull(subscription);
        Assert.Equal(SubscriptionState.Unknown, subscription!.State);
        Assert.Equal("some_future_state", subscription.ProviderState);

        // An unmapped state must never look actionable.
        Assert.Empty(subscription.AllowedActions);
        Assert.False(subscription.CanRecordUsage);
        Assert.False(subscription.CanChangePlan);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ReportsAPendingCancellation()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.SubscriptionPendingCancel);

        var subscription = await context.Client.GetSubscriptionAsync(MaxioPayloads.SubscriptionId);

        Assert.NotNull(subscription);
        Assert.True(subscription!.CancellationPending);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)),
            subscription.CancellationScheduledAt);

        // A second end-of-period cancel is pointless once one is already scheduled.
        Assert.DoesNotContain(SubscriptionLifecycleAction.CancelAtEndOfPeriod, subscription.AllowedActions);
        Assert.Contains(SubscriptionLifecycleAction.CancelImmediately, subscription.AllowedActions);
    }
}
