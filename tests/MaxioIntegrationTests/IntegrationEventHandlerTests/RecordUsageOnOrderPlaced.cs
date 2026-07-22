using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.IntegrationEventHandlerTests;

/// <summary>
/// The "one order placed → one billable unit" hook runs on eShopOnWeb's checkout path. These tests
/// pin the guarantee that matters most: a billing problem can never propagate out of this handler
/// and disturb the order lifecycle.
/// </summary>
public class RecordUsageOnOrderPlaced
{
    private const string BuyerId = "demouser@microsoft.com";

    private readonly ISubscriptionService _subscriptionService = Substitute.For<ISubscriptionService>();
    private readonly IAppLogger<RecordUsageOnOrderPlacedHandler> _logger =
        Substitute.For<IAppLogger<RecordUsageOnOrderPlacedHandler>>();

    private RecordUsageOnOrderPlacedHandler Handler => new(_subscriptionService, _logger);

    [Fact]
    public async Task RecordsExactlyOneUnitPerOrder()
    {
        _subscriptionService
            .RecordUsageAsync(BuyerId, Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(UsageSummary.WithoutTotal(new UsageRecord(900, 42, 1m)));

        await Handler.Handle(new OrderPlaced(17, BuyerId), CancellationToken.None);

        await _subscriptionService.Received(1)
            .RecordUsageAsync(BuyerId, 1m, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IdentifiesTheOrderInTheUsageMemo()
    {
        _subscriptionService
            .RecordUsageAsync(BuyerId, Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(UsageSummary.WithoutTotal(new UsageRecord(900, 42, 1m)));

        await Handler.Handle(new OrderPlaced(17, BuyerId), CancellationToken.None);

        await _subscriptionService.Received(1).RecordUsageAsync(BuyerId, 1m,
            Arg.Is<string?>(memo => memo != null && memo.Contains("17")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StaysSilentForABuyerWithoutASubscription()
    {
        // Most eShopOnWeb shoppers have no subscription; that is normal, not an error.
        _subscriptionService
            .RecordUsageAsync(BuyerId, Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NoActiveSubscriptionException(BuyerId));

        await Handler.Handle(new OrderPlaced(17, BuyerId), CancellationToken.None);

        _logger.DidNotReceiveWithAnyArgs().LogWarning(default!);
    }

    [Fact]
    public async Task SwallowsAProviderOutageSoCheckoutIsNeverDisturbed()
    {
        _subscriptionService
            .RecordUsageAsync(BuyerId, Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("CreateUsage", 0, "unreachable"));

        // The order is already committed. This must complete, not throw.
        await Handler.Handle(new OrderPlaced(17, BuyerId), CancellationToken.None);

        _logger.ReceivedWithAnyArgs(1).LogWarning(default!);
    }

    [Fact]
    public async Task SwallowsAMisconfiguredBillingCatalog()
    {
        _subscriptionService
            .RecordUsageAsync(BuyerId, Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingConfigurationException("Component is not metered."));

        await Handler.Handle(new OrderPlaced(17, BuyerId), CancellationToken.None);
    }

    [Fact]
    public async Task SwallowsAnUnforeseenFailure()
    {
        _subscriptionService
            .RecordUsageAsync(BuyerId, Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("something nobody predicted"));

        await Handler.Handle(new OrderPlaced(17, BuyerId), CancellationToken.None);
    }

    [Fact]
    public async Task DoesNothingForAnAnonymousBuyer()
    {
        await Handler.Handle(new OrderPlaced(17, string.Empty), CancellationToken.None);

        await _subscriptionService.DidNotReceiveWithAnyArgs()
            .RecordUsageAsync(default!, default, default, default);
    }
}
