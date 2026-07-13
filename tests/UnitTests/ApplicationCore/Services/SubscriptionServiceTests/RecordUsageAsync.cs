using System;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class RecordUsageAsync
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPlanChangePreviewCache _previewCache = Substitute.For<IPlanChangePreviewCache>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService CreateSut() => new(_billingClient, _previewCache, _publisher, _logger);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RejectsNonPositiveQuantity_BeforeCallingTheProvider(double quantity)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            sut.RecordUsageAsync(1, "buyer@example.com", isAdmin: false, quantity, memo: null));

        await _billingClient.DidNotReceiveWithAnyArgs().GetSubscriptionAsync(default);
        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default, default);
    }

    [Fact]
    public async Task RejectsUsage_WhenSubscriptionIsNotActive()
    {
        var subscription = new BillingSubscription(1, 10, "buyer@example.com", 7111477, "eshop-pro", "Pro Plan", "canceled", 29900, null, null, null);
        _billingClient.GetSubscriptionAsync(1).Returns(subscription);

        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() =>
            sut.RecordUsageAsync(1, "buyer@example.com", isAdmin: false, quantity: 1, memo: null));

        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default, default);
    }

    [Fact]
    public async Task RejectsUsage_WhenSubscriptionBelongsToAnotherUser()
    {
        var subscription = new BillingSubscription(1, 10, "owner@example.com", 7111477, "eshop-pro", "Pro Plan", "active", 29900, null, null, null);
        _billingClient.GetSubscriptionAsync(1).Returns(subscription);

        var sut = CreateSut();

        await Assert.ThrowsAsync<SubscriptionAccessDeniedException>(() =>
            sut.RecordUsageAsync(1, "someoneelse@example.com", isAdmin: false, quantity: 1, memo: null));
    }

    [Fact]
    public async Task RecordsUsage_AndReportsPeriodToDateTotal_OnHappyPath()
    {
        var subscription = new BillingSubscription(1, 10, "buyer@example.com", 7111477, "eshop-pro", "Pro Plan", "active", 29900, null, null, null);
        _billingClient.GetSubscriptionAsync(1).Returns(subscription);
        _billingClient.RecordUsageAsync(1, 3, "checkout").Returns(new BillingUsage(555, 3, "checkout"));
        _billingClient.TryGetComponentPeriodToDateUsageAsync(1).Returns(42);

        var sut = CreateSut();
        var result = await sut.RecordUsageAsync(1, "buyer@example.com", isAdmin: false, quantity: 3, memo: "checkout");

        Assert.Equal(555, result.UsageId);
        Assert.True(result.PeriodToDateAvailable);
        Assert.Equal(42, result.PeriodToDateUnits);
    }
}
