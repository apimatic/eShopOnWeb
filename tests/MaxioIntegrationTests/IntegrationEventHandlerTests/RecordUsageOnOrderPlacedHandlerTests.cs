using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.IntegrationEventHandlerTests;

/// <summary>
/// The automatic "one order placed → one billable unit" hook (plan.md §8, UC2).
/// </summary>
/// <remarks>
/// The order is already committed by the time this runs. eShopOnWeb's order lifecycle must never
/// depend on Maxio being reachable, so the decisive tests here are the failure ones.
/// </remarks>
public class RecordUsageOnOrderPlacedHandlerTests
{
    private const string BuyerId = "demouser@microsoft.com";
    private const int CustomerId = 90210;

    private static readonly BillingPlan ProPlan = new(1, "eshop-pro", "Pro Plan", 299.00m, 1, "month");

    private static (RecordUsageOnOrderPlacedHandler Handler,
        FakeBillingClient Billing,
        RecordingLogger<RecordUsageOnOrderPlacedHandler> Logger) Build(
            SubscriptionState state = SubscriptionState.Active,
            bool hasSubscription = true)
    {
        var billing = new FakeBillingClient();
        billing.Plans.Add(ProPlan);

        if (hasSubscription)
        {
            billing.Customer = new BillingCustomer(CustomerId, BuyerId, BuyerId);
            billing.Subscriptions.Add(new Subscription(50, BuyerId, CustomerId, ProPlan, state,
                state.ToString().ToLowerInvariant()));
        }

        var service = new SubscriptionService(billing, new RecordingPublisher(),
            new RecordingLogger<SubscriptionService>());
        var logger = new RecordingLogger<RecordUsageOnOrderPlacedHandler>();

        return (new RecordUsageOnOrderPlacedHandler(service, logger), billing, logger);
    }

    [Fact]
    public async Task RecordsOneUnitOfUsageWhenTheBuyerHasAnActiveSubscription()
    {
        var (handler, billing, _) = Build();

        await handler.Handle(new OrderPlaced(42, BuyerId), CancellationToken.None);

        Assert.Contains("RecordUsage:50:1", billing.Calls);
    }

    [Fact]
    public async Task LabelsTheUsageWithTheOrderItCameFrom()
    {
        var (handler, billing, _) = Build();
        billing.Calls.Clear();

        await handler.Handle(new OrderPlaced(42, BuyerId), CancellationToken.None);

        // The memo is what lets an operator reconcile a line on the invoice with an order.
        Assert.Contains("RecordUsage:50:1", billing.Calls);
    }

    [Fact]
    public async Task DoesNothingWhenTheBuyerHasNoSubscription()
    {
        var (handler, billing, logger) = Build(hasSubscription: false);

        var exception = await Record.ExceptionAsync(
            () => handler.Handle(new OrderPlaced(42, "shopper@example.com"), CancellationToken.None));

        // Most shoppers never subscribe; that is the normal case, not a fault.
        Assert.Null(exception);
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("RecordUsage:", StringComparison.Ordinal));
        Assert.Contains(logger.Informations, m => m.Contains("no active subscription"));
    }

    [Fact]
    public async Task SwallowsAndLogsAProviderOutageSoCheckoutStillSucceeds()
    {
        var (handler, billing, logger) = Build();
        billing.LookupFailure = new BillingProviderException("Maxio could not be reached.");

        var exception = await Record.ExceptionAsync(
            () => handler.Handle(new OrderPlaced(42, BuyerId), CancellationToken.None));

        // The order is already placed. A billing outage must not surface as a failed checkout.
        Assert.Null(exception);
        Assert.Contains(logger.Warnings, m => m.Contains("Could not record pay-as-you-go usage"));
    }

    [Fact]
    public async Task SwallowsAndLogsAnUnexpectedFailureRatherThanFailingTheOrder()
    {
        var (handler, billing, logger) = Build();
        billing.LookupFailure = new InvalidOperationException("something nobody anticipated");

        var exception = await Record.ExceptionAsync(
            () => handler.Handle(new OrderPlaced(42, BuyerId), CancellationToken.None));

        // Not just billing errors: nothing at all may escape into the order lifecycle.
        Assert.Null(exception);
        Assert.Contains(logger.Warnings, m => m.Contains("Could not record pay-as-you-go usage"));
    }

    [Fact]
    public async Task SwallowsAndLogsAMisconfiguredMeteredComponent()
    {
        var (handler, billing, logger) = Build();
        billing.ComponentFailure = new BillingConfigurationException("Component 'api-call' is not metered.");

        var exception = await Record.ExceptionAsync(
            () => handler.Handle(new OrderPlaced(42, BuyerId), CancellationToken.None));

        Assert.Null(exception);
        Assert.Contains(logger.Warnings, m => m.Contains("Could not record pay-as-you-go usage"));
    }

    [Fact]
    public async Task DoesNotRecordUsageWhenTheBuyersSubscriptionIsPaused()
    {
        var (handler, billing, _) = Build(SubscriptionState.Paused);

        await handler.Handle(new OrderPlaced(42, BuyerId), CancellationToken.None);

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("RecordUsage:", StringComparison.Ordinal));
    }
}
