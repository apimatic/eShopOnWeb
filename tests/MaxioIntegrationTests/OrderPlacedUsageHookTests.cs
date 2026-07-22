using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// UC2's automatic trigger: one order placed records one billable unit. The order has already been
/// persisted by the time this runs, so the handler must be total — no billing condition may surface as
/// a failed checkout.
/// </summary>
public class OrderPlacedUsageHookTests
{
    private const string BuyerId = "demouser@microsoft.com";

    private static (RecordUsageOnOrderPlacedHandler Handler, FakeBillingClient Billing) Build()
    {
        var billing = new FakeBillingClient();
        billing.Plans.Add(new SubscriptionPlan(7130995, "eshop-pro", "Pro Plan", 299.00m, 1, "month"));

        var service = new SubscriptionService(billing, new RecordingPublisher(), new NullAppLogger<SubscriptionService>());
        var handler = new RecordUsageOnOrderPlacedHandler(service, new NullAppLogger<RecordUsageOnOrderPlacedHandler>());

        return (handler, billing);
    }

    private static CustomerSubscription Active(int id) => new(id, SubscriptionLifecycleState.Active)
    {
        PlanHandle = "eshop-pro",
        PlanPrice = 299.00m,
        CustomerReference = BuyerId
    };

    [Fact]
    public async Task An_order_placed_by_a_subscriber_records_exactly_one_unit()
    {
        var (handler, billing) = Build();
        billing.Subscriptions.Add(Active(1001));

        await handler.Handle(new OrderPlaced(42, BuyerId), CancellationToken.None);

        Assert.Contains("RecordUsageAsync:1001:1:eShopOnWeb order 42", billing.Calls);
    }

    [Fact]
    public async Task The_memo_identifies_the_order_the_unit_came_from()
    {
        var (handler, billing) = Build();
        billing.Subscriptions.Add(Active(1001));

        await handler.Handle(new OrderPlaced(4242, BuyerId), CancellationToken.None);

        var recorded = Assert.Single(billing.Calls.Where(c => c.StartsWith("RecordUsageAsync", StringComparison.Ordinal)));
        Assert.Contains("4242", recorded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_order_from_a_buyer_with_no_subscription_records_nothing()
    {
        var (handler, billing) = Build();

        await handler.Handle(new OrderPlaced(42, BuyerId), CancellationToken.None);

        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("RecordUsageAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_anonymous_cookie_basket_is_skipped_without_contacting_the_provider()
    {
        var (handler, billing) = Build();

        await handler.Handle(new OrderPlaced(42, Guid.NewGuid().ToString()), CancellationToken.None);

        // An anonymous buyer can never map to a billing customer, so no lookup is even attempted.
        Assert.Empty(billing.Calls);
    }

    [Fact]
    public async Task A_billing_outage_never_propagates_out_of_the_order_lifecycle()
    {
        var (handler, billing) = Build();
        billing.Subscriptions.Add(Active(1001));
        billing.PeriodToDateFailure = new BillingUnavailableException(
            "GetPeriodToDateUsageAsync", new HttpRequestException("no route to host"));

        // The order is already persisted: this must complete quietly rather than throw.
        await handler.Handle(new OrderPlaced(42, BuyerId), CancellationToken.None);

        Assert.Contains("RecordUsageAsync:1001:1:eShopOnWeb order 42", billing.Calls);
    }

    [Fact]
    public async Task A_misconfigured_metered_component_never_propagates_either()
    {
        var (handler, billing) = Build();
        billing.Subscriptions.Add(Active(1001));
        billing.Component = new MeteredComponent(0, "api-call", "API Calls", "on_off_component", isMetered: false, 0m);

        await handler.Handle(new OrderPlaced(42, BuyerId), CancellationToken.None);

        // It refused to bill, and it refused to break checkout doing so.
        Assert.DoesNotContain(billing.Calls, c => c.StartsWith("RecordUsageAsync", StringComparison.Ordinal));
    }
}
