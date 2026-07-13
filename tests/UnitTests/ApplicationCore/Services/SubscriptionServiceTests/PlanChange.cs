using System;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class PlanChange
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPlanChangePreviewCache _previewCache = Substitute.For<IPlanChangePreviewCache>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService CreateSut() => new(_billingClient, _previewCache, _publisher, _logger);

    private static BillingSubscription ActiveSubscription() =>
        new(1, 10, "buyer@example.com", 7111477, "eshop-pro", "Pro Plan", "active", 29900, null, null, null);

    [Fact]
    public async Task PreviewRejectsAsNoOp_WhenTargetPlanIsTheCurrentPlan()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(ActiveSubscription());
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() =>
            sut.PreviewPlanChangeAsync(1, "buyer@example.com", isAdmin: false, "eshop-pro", applyAtRenewal: false));
    }

    [Fact]
    public async Task PreviewThrows_WhenTargetPlanHandleDoesNotResolve()
    {
        _billingClient.GetSubscriptionAsync(1).Returns(ActiveSubscription());
        _billingClient.GetPlanByHandleAsync("does-not-exist").Returns((BillingPlan?)null);
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.PreviewPlanChangeAsync(1, "buyer@example.com", isAdmin: false, "does-not-exist", applyAtRenewal: false));
    }

    [Fact]
    public async Task CommitRejects_WhenPreviewTokenIsUnknownOrExpired()
    {
        _previewCache.TryConsume(Arg.Any<Guid>()).Returns((PlanChangePreviewEntry?)null);
        var sut = CreateSut();

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(() =>
            sut.CommitPlanChangeAsync(1, "buyer@example.com", isAdmin: false, Guid.NewGuid()));

        await _billingClient.DidNotReceiveWithAnyArgs().CommitPlanChangeNowAsync(default!, default!);
    }

    [Fact]
    public async Task CommitRejects_WhenSubscriptionsPlanDriftedSincePreview()
    {
        var token = Guid.NewGuid();
        _previewCache.TryConsume(token).Returns(new PlanChangePreviewEntry(1, "eshop-pro", "basic-plan", false, DateTimeOffset.UtcNow.AddMinutes(5)));
        // Subscription has since moved off 'eshop-pro' out of band.
        _billingClient.GetSubscriptionAsync(1).Returns(new BillingSubscription(1, 10, "buyer@example.com", 1, "basic-plan", "Basic Plan", "active", 2900, null, null, null));
        var sut = CreateSut();

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(() =>
            sut.CommitPlanChangeAsync(1, "buyer@example.com", isAdmin: false, token));
    }

    [Fact]
    public async Task CommitAppliesImmediateMigration_AndPublishesPlanChanged_OnHappyPath()
    {
        var token = Guid.NewGuid();
        _previewCache.TryConsume(token).Returns(new PlanChangePreviewEntry(1, "eshop-pro", "basic-plan", false, DateTimeOffset.UtcNow.AddMinutes(5)));
        _billingClient.GetSubscriptionAsync(1).Returns(ActiveSubscription());
        _billingClient.CommitPlanChangeNowAsync(1, "basic-plan").Returns(
            new BillingSubscription(1, 10, "buyer@example.com", 7111478, "basic-plan", "Basic Plan", "active", 2900, null, null, null));

        var sut = CreateSut();
        var result = await sut.CommitPlanChangeAsync(1, "buyer@example.com", isAdmin: false, token);

        Assert.Equal("basic-plan", result.ProductHandle);
        await _publisher.Received(1).Publish(
            Arg.Is<object>(n => n.GetType() == typeof(Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.SubscriptionPlanChanged)
                && ((Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.SubscriptionPlanChanged)n).FromProductHandle == "eshop-pro"
                && ((Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.SubscriptionPlanChanged)n).ToProductHandle == "basic-plan"),
            Arg.Any<System.Threading.CancellationToken>());
    }
}
