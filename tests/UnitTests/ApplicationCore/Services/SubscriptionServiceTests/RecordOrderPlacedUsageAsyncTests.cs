using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class RecordOrderPlacedUsageAsyncTests
{
    private readonly IBillingClient _mockBillingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _mockPublisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionBuilder _builder = new();

    private SubscriptionService CreateService() => new(_mockBillingClient, _mockPublisher, _mockLogger);

    [Fact]
    public async Task RecordsOneUnitAgainstTheBuyersActiveSubscription()
    {
        var active = _builder.Active();
        _mockBillingClient.FindSubscriptionByCustomerReferenceAsync(SubscriptionBuilder.TestBuyerId, Arg.Any<CancellationToken>())
            .Returns(active);

        var service = CreateService();
        await service.RecordOrderPlacedUsageAsync(SubscriptionBuilder.TestBuyerId);

        await _mockBillingClient.Received(1).RecordUsageAsync(active.Id, 1, "Order placed", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNothingWhenTheBuyerHasNoSubscription()
    {
        _mockBillingClient.FindSubscriptionByCustomerReferenceAsync(SubscriptionBuilder.TestBuyerId, Arg.Any<CancellationToken>())
            .Returns((Subscription?)null);

        var service = CreateService();
        await service.RecordOrderPlacedUsageAsync(SubscriptionBuilder.TestBuyerId);

        await _mockBillingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<double>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NeverThrowsEvenWhenTheProviderCallFails()
    {
        var active = _builder.Active();
        _mockBillingClient.FindSubscriptionByCustomerReferenceAsync(SubscriptionBuilder.TestBuyerId, Arg.Any<CancellationToken>())
            .Returns(active);
        _mockBillingClient.RecordUsageAsync(active.Id, 1, "Order placed", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<UsageRecord>(new InvalidOperationException("provider unreachable")));

        var service = CreateService();

        // Must not throw - an order has already been placed successfully (plan.md §2.5).
        await service.RecordOrderPlacedUsageAsync(SubscriptionBuilder.TestBuyerId);
    }
}
