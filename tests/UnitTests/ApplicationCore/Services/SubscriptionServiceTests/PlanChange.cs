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

public class PlanChange
{
    private readonly IBillingClient _mockBillingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _mockPublisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService CreateSubscriptionService() =>
        new(_mockBillingClient, _mockPublisher, _mockLogger);

    private static CustomerSubscription ActiveSubscriptionOnPro() =>
        new(10, "buyer@test.com", SubscriptionStates.Active, "eshop-pro", "Pro Plan", 29900, null, null, false, null, 0);

    [Fact]
    public async Task PreviewPlanChangeAsync_WhenTargetPlanIsSameAsCurrent_ThrowsBeforeAnyPreviewCall()
    {
        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>())
            .Returns(ActiveSubscriptionOnPro());

        var subscriptionService = CreateSubscriptionService();

        await Assert.ThrowsAsync<SubscriptionConflictException>(() =>
            subscriptionService.PreviewPlanChangeAsync("buyer@test.com", 10, "eshop-pro"));

        await _mockBillingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_WhenSubscriptionIsCancelled_ThrowsConflict()
    {
        var cancelled = new CustomerSubscription(10, "buyer@test.com", SubscriptionStates.Canceled, "eshop-pro",
            "Pro Plan", 29900, null, null, false, null, 0);
        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>()).Returns(cancelled);

        var subscriptionService = CreateSubscriptionService();

        await Assert.ThrowsAsync<SubscriptionConflictException>(() =>
            subscriptionService.PreviewPlanChangeAsync("buyer@test.com", 10, "basic-plan"));
    }

    [Fact]
    public async Task CommitPlanChangeAsync_WhenPreviewedAmountNoLongerMatches_RejectsAsStale()
    {
        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>())
            .Returns(ActiveSubscriptionOnPro());
        _mockBillingClient.PreviewPlanChangeAsync(10, "basic-plan", Arg.Any<CancellationToken>())
            .Returns(new PlanChangePreview("basic-plan", -5000, 0, 0, 5000));

        var subscriptionService = CreateSubscriptionService();

        await Assert.ThrowsAsync<SubscriptionConflictException>(() =>
            subscriptionService.CommitPlanChangeAsync("buyer@test.com", 10, "basic-plan", PlanChangeTiming.Now,
                expectedProratedAdjustmentInCents: -4000));

        await _mockBillingClient.DidNotReceive().ApplyPlanChangeNowAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitPlanChangeAsync_WhenPreviewedAmountMatches_AppliesTheChangeNow()
    {
        var updated = new CustomerSubscription(10, "buyer@test.com", SubscriptionStates.Active, "basic-plan",
            "Basic Plan", 2900, null, null, false, null, 0);

        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>())
            .Returns(ActiveSubscriptionOnPro());
        _mockBillingClient.PreviewPlanChangeAsync(10, "basic-plan", Arg.Any<CancellationToken>())
            .Returns(new PlanChangePreview("basic-plan", -5000, 0, 0, 5000));
        _mockBillingClient.ApplyPlanChangeNowAsync(10, "basic-plan", Arg.Any<CancellationToken>())
            .Returns(updated);

        var subscriptionService = CreateSubscriptionService();

        var result = await subscriptionService.CommitPlanChangeAsync("buyer@test.com", 10, "basic-plan",
            PlanChangeTiming.Now, expectedProratedAdjustmentInCents: -5000);

        Assert.Equal("basic-plan", result.PlanHandle);
    }

    [Fact]
    public async Task CommitPlanChangeAsync_AtNextRenewal_SchedulesInsteadOfPreviewingProration()
    {
        var updated = ActiveSubscriptionOnPro();

        _mockBillingClient.GetSubscriptionAsync(10, Arg.Any<CancellationToken>())
            .Returns(ActiveSubscriptionOnPro());
        _mockBillingClient.SchedulePlanChangeAtRenewalAsync(10, "basic-plan", Arg.Any<CancellationToken>())
            .Returns(updated);

        var subscriptionService = CreateSubscriptionService();

        await subscriptionService.CommitPlanChangeAsync("buyer@test.com", 10, "basic-plan",
            PlanChangeTiming.AtNextRenewal, expectedProratedAdjustmentInCents: null);

        await _mockBillingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _mockBillingClient.Received(1).SchedulePlanChangeAtRenewalAsync(10, "basic-plan",
            Arg.Any<CancellationToken>());
    }
}
