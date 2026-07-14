using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class CommitPlanChangeAsync
{
    private const string BuyerId = "buyer@example.com";
    private const string CurrentHandle = "eshop-pro";
    private const string TargetHandle = "basic-plan";

    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private static Subscription MakeSubscription(SubscriptionStatus status) => new(
        1, BuyerId, BuyerId, CurrentHandle, "Pro Plan", 29900, status, null, null, false, null, null);

    private static PlanChangePreview MakePreview(long comparableAmountInCents) => new(
        1, CurrentHandle, TargetHandle, PlanChangeTiming.Now, comparableAmountInCents, comparableAmountInCents, null, null, DateTimeOffset.UtcNow);

    private SubscriptionService CreateSut() => new(_billingClient, _publisher, _logger);

    [Fact]
    public async Task Commits_WhenFreshPreviewMatchesPreviewedAmount()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(MakeSubscription(SubscriptionStatus.Active));
        _billingClient.ListPlansAsync().Returns(new List<BillingPlan> { new(TargetHandle, "Basic", 2900, 1, BillingIntervalUnit.Month, false) });
        _billingClient.PreviewPlanChangeAsync(1, CurrentHandle, TargetHandle, PlanChangeTiming.Now).Returns(MakePreview(-27000));
        var updated = new Subscription(1, BuyerId, BuyerId, TargetHandle, "Basic", 2900, SubscriptionStatus.Active, null, null, false, null, null);
        _billingClient.ApplyPlanChangeNowAsync(1, TargetHandle).Returns(updated);

        var sut = CreateSut();
        var result = await sut.CommitPlanChangeAsync(1, BuyerId, isAdmin: false, TargetHandle, PlanChangeTiming.Now, -27000);

        Assert.Equal(TargetHandle, result.ProductHandle);
    }

    [Fact]
    public async Task Throws_WhenFreshPreviewNoLongerMatchesPreviewedAmount()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(MakeSubscription(SubscriptionStatus.Active));
        _billingClient.ListPlansAsync().Returns(new List<BillingPlan> { new(TargetHandle, "Basic", 2900, 1, BillingIntervalUnit.Month, false) });
        _billingClient.PreviewPlanChangeAsync(1, CurrentHandle, TargetHandle, PlanChangeTiming.Now).Returns(MakePreview(-27000));

        var sut = CreateSut();

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(
            () => sut.CommitPlanChangeAsync(1, BuyerId, isAdmin: false, TargetHandle, PlanChangeTiming.Now, -20000));
        await _billingClient.DidNotReceive().ApplyPlanChangeNowAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Throws_WhenTargetPlanIsSameAsCurrentPlan()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(MakeSubscription(SubscriptionStatus.Active));

        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.CommitPlanChangeAsync(1, BuyerId, isAdmin: false, CurrentHandle, PlanChangeTiming.Now, 0));
    }

    [Fact]
    public async Task Throws_WhenSubscriptionIsCancelled()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(MakeSubscription(SubscriptionStatus.Canceled));

        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(
            () => sut.CommitPlanChangeAsync(1, BuyerId, isAdmin: false, TargetHandle, PlanChangeTiming.Now, 0));
    }
}
