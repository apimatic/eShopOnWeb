using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The in-process reactions to subscription events (plan.md §2.5), including UC2's automatic
/// "one order placed meters one unit" trigger.
/// </summary>
public class SubscriptionEventHandlerTests
{
    private const string BuyerId = "demouser@microsoft.com";

    private readonly ISubscriptionService _subscriptionService = Substitute.For<ISubscriptionService>();
    private readonly IAppLogger<RecordOrderUsageHandler> _logger = Substitute.For<IAppLogger<RecordOrderUsageHandler>>();

    private RecordOrderUsageHandler CreateHandler() => new(_subscriptionService, _logger);

    private static Subscription LiveSubscription(int id = 93462813) => new(
        id, 14543792, BuyerId, SubscriptionState.Active, "eshop-pro", "Pro Plan", 29900,
        DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(29), false, null, null);

    private static Subscription CancelledSubscription(int id = 93462814) => new(
        id, 14543792, BuyerId, SubscriptionState.Canceled, "eshop-pro", "Pro Plan", 29900,
        null, null, false, DateTimeOffset.UtcNow, null);

    [Fact]
    public async Task AnOrderMetersExactlyOneUnitAgainstTheBuyersLiveSubscription()
    {
        _subscriptionService.GetSubscriptionsForUserAsync(BuyerId, Arg.Any<CancellationToken>())
            .Returns(new[] { LiveSubscription() });

        await CreateHandler().Handle(new OrderPlaced(4321, BuyerId), CancellationToken.None);

        await _subscriptionService.Received(1).RecordUsageAsync(93462813, 1m, "Order 4321", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOrderFromABuyerWithNoSubscriptionMetersNothing()
    {
        _subscriptionService.GetSubscriptionsForUserAsync(BuyerId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());

        await CreateHandler().Handle(new OrderPlaced(4321, BuyerId), CancellationToken.None);

        await _subscriptionService.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOrderFromABuyerWhoseSubscriptionIsCancelledMetersNothing()
    {
        _subscriptionService.GetSubscriptionsForUserAsync(BuyerId, Arg.Any<CancellationToken>())
            .Returns(new[] { CancelledSubscription() });

        await CreateHandler().Handle(new OrderPlaced(4321, BuyerId), CancellationToken.None);

        await _subscriptionService.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheLiveSubscriptionIsChosenWhenTheBuyerAlsoHasDeadOnes()
    {
        _subscriptionService.GetSubscriptionsForUserAsync(BuyerId, Arg.Any<CancellationToken>())
            .Returns(new[] { CancelledSubscription(), LiveSubscription(93462999) });

        await CreateHandler().Handle(new OrderPlaced(4321, BuyerId), CancellationToken.None);

        await _subscriptionService.Received(1).RecordUsageAsync(93462999, 1m, "Order 4321", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMeteringFailureNeverPropagatesOutAndFailsTheOrder()
    {
        _subscriptionService.GetSubscriptionsForUserAsync(BuyerId, Arg.Any<CancellationToken>())
            .Returns(new[] { LiveSubscription() });
        _subscriptionService.RecordUsageAsync(Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("provider is down"));

        // The order has already been committed; metering is best-effort (plan.md §2.5).
        await CreateHandler().Handle(new OrderPlaced(4321, BuyerId), CancellationToken.None);

        _logger.ReceivedWithAnyArgs(1).LogWarning(default!);
    }

    [Fact]
    public async Task AFailureListingSubscriptionsAlsoNeverFailsTheOrder()
    {
        _subscriptionService.GetSubscriptionsForUserAsync(BuyerId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("provider is down"));

        await CreateHandler().Handle(new OrderPlaced(4321, BuyerId), CancellationToken.None);

        _logger.ReceivedWithAnyArgs(1).LogWarning(default!);
    }

    [Fact]
    public async Task EveryLifecycleEventIsWrittenToTheAuditTrail()
    {
        var logger = Substitute.For<IAppLogger<SubscriptionAuditLogHandler>>();
        var handler = new SubscriptionAuditLogHandler(logger);
        var subscription = LiveSubscription();

        await handler.Handle(new SubscriptionActivated(subscription), CancellationToken.None);
        await handler.Handle(new SubscriptionPlanChanged(subscription, "basic-plan", PlanChangeTiming.Immediately, null),
            CancellationToken.None);
        await handler.Handle(new SubscriptionStateChanged(subscription, SubscriptionState.OnHold, SubscriptionActions.Resume),
            CancellationToken.None);

        logger.ReceivedWithAnyArgs(3).LogInformation(default!);
    }
}
