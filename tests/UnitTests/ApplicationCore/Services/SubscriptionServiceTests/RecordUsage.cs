using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class RecordUsage
{
    private readonly IBillingClient _mockBillingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _mockPublisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService CreateSubscriptionService() =>
        new(_mockBillingClient, _mockPublisher, _mockLogger);

    private static CustomerSubscription ActiveSubscription(int id = 10) =>
        new(id, "buyer@test.com", SubscriptionStates.Active, "eshop-pro", "Pro Plan", 29900, null, null, false, null, 0);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task WhenQuantityIsNotPositive_ThrowsBeforeAnyProviderCall(double quantity)
    {
        var subscriptionService = CreateSubscriptionService();

        await Assert.ThrowsAsync<InvalidSubscriptionRequestException>(() =>
            subscriptionService.RecordUsageAsync("buyer@test.com", 10, quantity, null));

        await _mockBillingClient.DidNotReceive().GetSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenCallerDoesNotOwnTheSubscription_ThrowsSubscriptionNotFound()
    {
        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>())
            .Returns(ActiveSubscription());

        var subscriptionService = CreateSubscriptionService();

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(() =>
            subscriptionService.RecordUsageAsync("someone-else@test.com", 10, 1, null));

        await _mockBillingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<double>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenSubscriptionIsNotActiveOrTrialing_ThrowsSubscriptionConflict()
    {
        var pausedSubscription = new CustomerSubscription(10, "buyer@test.com", SubscriptionStates.OnHold,
            "eshop-pro", "Pro Plan", 29900, null, null, false, null, 0);
        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>()).Returns(pausedSubscription);

        var subscriptionService = CreateSubscriptionService();

        await Assert.ThrowsAsync<SubscriptionConflictException>(() =>
            subscriptionService.RecordUsageAsync("buyer@test.com", 10, 1, null));
    }

    [Fact]
    public async Task WhenValid_RecordsUsageAndCombinesTheBalanceReadBack()
    {
        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>()).Returns(ActiveSubscription());
        _mockBillingClient.RecordUsageAsync(10, 3, "order #1", Arg.Any<CancellationToken>())
            .Returns(new UsageRecordResult(99, 3, System.DateTimeOffset.UtcNow, null));
        _mockBillingClient.TryGetMeteredComponentBalanceAsync(10, Arg.Any<CancellationToken>()).Returns(42);

        var subscriptionService = CreateSubscriptionService();

        var result = await subscriptionService.RecordUsageAsync("buyer@test.com", 10, 3, "order #1");

        Assert.Equal(99, result.UsageId);
        Assert.Equal(42, result.PeriodToDateBalance);
        await _mockBillingClient.Received(1).EnsureMeteredComponentIsValidAsync(Arg.Any<CancellationToken>());
    }
}
