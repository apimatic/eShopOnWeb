using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The automatic "one order placed, one billable unit" hook. Its overriding obligation is that no billing
/// problem can ever surface into eShopOnWeb's order lifecycle.
/// </summary>
public class OrderPlacedUsageHandlerTests
{
    private readonly ISubscriptionService _subscriptions = Substitute.For<ISubscriptionService>();

    private OrderPlacedUsageHandler CreateHandler() =>
        new(_subscriptions, Substitute.For<IAppLogger<OrderPlacedUsageHandler>>());

    [Fact]
    public async Task Meters_exactly_one_unit_per_order_and_notes_the_order_number()
    {
        _subscriptions.RecordUsageForUserAsync(SubscriptionFakes.USER, Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Report());

        await CreateHandler().Handle(new OrderPlaced(1001, SubscriptionFakes.USER), CancellationToken.None);

        await _subscriptions.Received(1).RecordUsageForUserAsync(
            SubscriptionFakes.USER,
            1m,
            Arg.Is<string?>(memo => memo != null && memo.Contains("1001")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_order_from_a_shopper_without_a_subscription_is_simply_not_metered()
    {
        _subscriptions.RecordUsageForUserAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidSubscriptionOperationException("no active subscription"));

        // Must not throw: the order is already placed.
        await CreateHandler().Handle(new OrderPlaced(1001, "shopper@example.com"), CancellationToken.None);
    }

    [Fact]
    public async Task An_unreachable_billing_provider_never_breaks_checkout()
    {
        _subscriptions.RecordUsageForUserAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new BillingProviderException("CreateUsage", "provider unreachable"));

        await CreateHandler().Handle(new OrderPlaced(1001, SubscriptionFakes.USER), CancellationToken.None);
    }

    [Fact]
    public async Task A_misconfigured_billing_catalog_never_breaks_checkout()
    {
        _subscriptions.RecordUsageForUserAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new BillingConfigurationException("component is not metered"));

        await CreateHandler().Handle(new OrderPlaced(1001, SubscriptionFakes.USER), CancellationToken.None);
    }

    [Fact]
    public async Task An_unexpected_failure_never_breaks_checkout()
    {
        _subscriptions.RecordUsageForUserAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("something nobody planned for"));

        await CreateHandler().Handle(new OrderPlaced(1001, SubscriptionFakes.USER), CancellationToken.None);
    }

    private static UsageReport Report()
    {
        return new UsageReport(SubscriptionFakes.UsageRecord(), SubscriptionFakes.SUBSCRIPTION_ID, 1m, 1, 0.01m, true);
    }
}
