using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

/// <summary>
/// The automatic "one order placed, one billable unit" hook. Metering is additive, so every failure
/// mode here must be absorbed — an order must never fail because billing did.
/// </summary>
public class RecordOrderUsageHandlerTests
{
    private const string BuyerId = "demouser@microsoft.com";

    private readonly ISubscriptionService _subscriptions = Substitute.For<ISubscriptionService>();
    private readonly IAppLogger<RecordOrderUsageHandler> _logger =
        Substitute.For<IAppLogger<RecordOrderUsageHandler>>();

    private RecordOrderUsageHandler CreateHandler() => new(_subscriptions, _logger);

    private static UsageReport Report() => new(
        new UsageRecord(1, 90210, 3062734, "api-call", 1m, "eShopOnWeb order 42", DateTimeOffset.UtcNow),
        10, 0.01m);

    [Fact]
    public async Task MetersExactlyOneUnitPerOrderAndLabelsItWithTheOrderNumber()
    {
        _subscriptions.RecordUsageAsync(BuyerId, 1m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Report());

        await CreateHandler().Handle(new OrderPlaced(42, BuyerId), CancellationToken.None);

        await _subscriptions.Received(1).RecordUsageAsync(BuyerId, 1m,
            Arg.Is<string>(memo => memo!.Contains("42")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNothingForACheckoutWithNoBuyer()
    {
        await CreateHandler().Handle(new OrderPlaced(42, ""), CancellationToken.None);

        await _subscriptions.DidNotReceive().RecordUsageAsync(Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StaysSilentForTheOrdinaryCaseOfABuyerWithNoSubscription()
    {
        _subscriptions.RecordUsageAsync(BuyerId, 1m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SubscriptionNotFoundException(BuyerId));

        await CreateHandler().Handle(new OrderPlaced(42, BuyerId), CancellationToken.None);

        _logger.DidNotReceive().LogWarning(Arg.Any<string>());
    }

    [Fact]
    public async Task AbsorbsAMisconfiguredBillingCatalogueSoTheOrderStillStands()
    {
        _subscriptions.RecordUsageAsync(BuyerId, 1m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingConfigurationException("api-call is not metered."));

        await CreateHandler().Handle(new OrderPlaced(42, BuyerId), CancellationToken.None);

        _logger.Received(1).LogWarning(Arg.Is<string>(m => m.Contains("42")));
    }

    [Fact]
    public async Task AbsorbsAnUnreachableProviderSoTheOrderStillStands()
    {
        _subscriptions.RecordUsageAsync(BuyerId, 1m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException(0, "Maxio could not be reached."));

        await CreateHandler().Handle(new OrderPlaced(42, BuyerId), CancellationToken.None);

        _logger.Received(1).LogWarning(Arg.Any<string>());
    }

    [Fact]
    public async Task AbsorbsAPausedSubscriptionSoTheOrderStillStands()
    {
        _subscriptions.RecordUsageAsync(BuyerId, 1m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidSubscriptionTransitionException(90210, SubscriptionState.Paused,
                SubscriptionLifecycleAction.Resume, Array.Empty<SubscriptionLifecycleAction>()));

        await CreateHandler().Handle(new OrderPlaced(42, BuyerId), CancellationToken.None);

        _logger.Received(1).LogWarning(Arg.Any<string>());
    }
}
