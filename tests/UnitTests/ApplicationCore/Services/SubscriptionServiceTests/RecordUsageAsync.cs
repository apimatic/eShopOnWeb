using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class RecordUsageAsync
{
    private readonly IBillingClient _mockBillingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _mockPublisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionBuilder _builder = new();

    private SubscriptionService CreateService() => new(_mockBillingClient, _mockPublisher, _mockLogger);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ThrowsArgumentException_WhenQuantityIsZeroOrNegative(int quantity)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<System.ArgumentException>(() =>
            service.RecordUsageAsync(1, _builder.TestOwnerReference, quantity, null));

        await _mockBillingClient.DidNotReceive().GetSubscriptionAsync(Arg.Any<int>(), default);
    }

    [Fact]
    public async Task ThrowsSubscriptionNotFoundException_WhenCallerDoesNotOwnTheSubscription()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Active, ownerReference: "someone-else@test.com");
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);

        var service = CreateService();

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(() =>
            service.RecordUsageAsync(1, _builder.TestOwnerReference, 1, null));
    }

    [Fact]
    public async Task ThrowsInvalidSubscriptionStateException_WhenSubscriptionIsNotActiveOrTrialing()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Paused);
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() =>
            service.RecordUsageAsync(1, _builder.TestOwnerReference, 1, null));

        await _mockBillingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), default);
    }

    [Fact]
    public async Task ValidatesComponentThenRecordsUsage_WhenSubscriptionIsActive()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Active);
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);
        var expectedReport = new UsageReport(1, 3, 3, true);
        _mockBillingClient.RecordUsageAsync(1, 3, "memo", default).Returns(expectedReport);

        var service = CreateService();

        var result = await service.RecordUsageAsync(1, _builder.TestOwnerReference, 3, "memo");

        Assert.Same(expectedReport, result);
        await _mockBillingClient.Received().EnsureMeteredComponentAsync(default);
        await _mockBillingClient.Received().RecordUsageAsync(1, 3, "memo", default);
    }

    [Fact]
    public async Task DoesNotEnforceOwnership_WhenOwnerUserIdIsNull_AdminBypass()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Active, ownerReference: "someone-else@test.com");
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);
        _mockBillingClient.RecordUsageAsync(1, 1, null, default).Returns(new UsageReport(1, 1, null, false));

        var service = CreateService();

        var result = await service.RecordUsageAsync(1, ownerUserId: null, 1, null);

        Assert.Equal(1, result.SubscriptionId);
    }
}
