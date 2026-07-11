using System;
using System.Threading;
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

public class RecordUsageAsyncTests
{
    private readonly IBillingClient _mockBillingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _mockPublisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionBuilder _builder = new();

    private SubscriptionService CreateService() => new(_mockBillingClient, _mockPublisher, _mockLogger);

    [Fact]
    public async Task RejectsZeroOrNegativeQuantityBeforeAnyProviderCall()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RecordUsageAsync(SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId, 0, null));

        await _mockBillingClient.DidNotReceive().GetSubscriptionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsUsageAgainstSomeoneElsesSubscriptionWhenNotAdmin()
    {
        var othersSubscription = _builder.WithBuyerId("someone-else@example.com");
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>())
            .Returns(othersSubscription);

        var service = CreateService();

        await Assert.ThrowsAsync<SubscriptionAccessDeniedException>(
            () => service.RecordUsageAsync(SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId, 1, null));
    }

    [Fact]
    public async Task RejectsUsageAgainstAnInactiveSubscription()
    {
        var paused = _builder.WithState("on_hold");
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>())
            .Returns(paused);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => service.RecordUsageAsync(SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId, 1, null));
    }

    [Fact]
    public async Task RecordsUsageAndReturnsThePeriodToDateSummary()
    {
        var active = _builder.Active();
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>())
            .Returns(active);
        var usage = new UsageRecord(1, "api-call", 5, "memo", DateTimeOffset.UtcNow);
        _mockBillingClient.RecordUsageAsync(SubscriptionBuilder.TestSubscriptionId, 5, "memo", Arg.Any<CancellationToken>())
            .Returns(usage);
        var summary = new UsagePeriodSummary("api-call", 5, true);
        _mockBillingClient.GetUsagePeriodToDateAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>())
            .Returns(summary);

        var service = CreateService();
        var (resultUsage, resultSummary) = await service.RecordUsageAsync(SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId, 5, "memo");

        Assert.Equal(usage.Id, resultUsage.Id);
        Assert.True(resultSummary.Available);
        Assert.Equal(5, resultSummary.PeriodToDateQuantity);
    }

    [Fact]
    public async Task AdminCanRecordUsageAgainstAnyCustomersSubscription()
    {
        var othersSubscription = _builder.WithBuyerId("someone-else@example.com");
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>())
            .Returns(othersSubscription);
        _mockBillingClient.RecordUsageAsync(SubscriptionBuilder.TestSubscriptionId, 1, null, Arg.Any<CancellationToken>())
            .Returns(new UsageRecord(1, "api-call", 1, null, DateTimeOffset.UtcNow));
        _mockBillingClient.GetUsagePeriodToDateAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>())
            .Returns(new UsagePeriodSummary("api-call", 1, true));

        var service = CreateService();
        await service.RecordUsageAsync("admin@example.com", isAdmin: true, SubscriptionBuilder.TestSubscriptionId, 1, null);

        await _mockBillingClient.Received(1).RecordUsageAsync(SubscriptionBuilder.TestSubscriptionId, 1, null, Arg.Any<CancellationToken>());
    }
}
