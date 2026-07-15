using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class PlanChange
{
    private readonly IBillingClient _mockBillingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _mockPublisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionBuilder _builder = new();

    private SubscriptionService CreateService() => new(_mockBillingClient, _mockPublisher, _mockLogger);

    [Fact]
    public async Task Preview_ThrowsArgumentException_WhenTargetIsTheCurrentPlan()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Active, productHandle: "eshop-pro");
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);

        var service = CreateService();

        await Assert.ThrowsAsync<System.ArgumentException>(() =>
            service.PreviewPlanChangeAsync(1, _builder.TestOwnerReference, "eshop-pro", PlanChangeTiming.Now));
    }

    [Fact]
    public async Task Preview_ThrowsInvalidSubscriptionStateException_WhenSubscriptionCannotChangePlan()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Canceled, productHandle: "eshop-pro");
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidSubscriptionStateException>(() =>
            service.PreviewPlanChangeAsync(1, _builder.TestOwnerReference, "basic-plan", PlanChangeTiming.Now));
    }

    [Fact]
    public async Task Commit_ThrowsStalePlanChangePreviewException_WhenPricingHasDrifted()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Active, productHandle: "eshop-pro");
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);
        _mockBillingClient.ListPlansAsync(default).Returns(new List<BillingPlan>
        {
            new(1, "eshop-pro", "Pro Plan", 29900, 1, "month"),
            new(2, "basic-plan", "Basic Plan", 2900, 1, "month")
        });

        var confirmedPreview = new PlanChangePreview(1, "eshop-pro", "basic-plan", PlanChangeTiming.Now, -2700, 0, 0, 2700, 2900, null);
        var freshPreview = new PlanChangePreview(1, "eshop-pro", "basic-plan", PlanChangeTiming.Now, -2600, 0, 0, 2600, 2900, null);
        _mockBillingClient.PreviewPlanChangeAsync(1, "eshop-pro", "basic-plan", PlanChangeTiming.Now, default).Returns(freshPreview);

        var service = CreateService();

        await Assert.ThrowsAsync<StalePlanChangePreviewException>(() =>
            service.CommitPlanChangeAsync(1, _builder.TestOwnerReference, confirmedPreview));

        await _mockBillingClient.DidNotReceive().CommitPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<PlanChangeTiming>(), default);
    }

    [Fact]
    public async Task Commit_AppliesChangeAndPublishesPlanChangedEvent_WhenPreviewMatches()
    {
        var subscription = _builder.WithState(1, SubscriptionState.Active, productHandle: "eshop-pro");
        _mockBillingClient.GetSubscriptionAsync(1, default).Returns(subscription);
        _mockBillingClient.ListPlansAsync(default).Returns(new List<BillingPlan>
        {
            new(1, "eshop-pro", "Pro Plan", 29900, 1, "month"),
            new(2, "basic-plan", "Basic Plan", 2900, 1, "month")
        });

        var confirmedPreview = new PlanChangePreview(1, "eshop-pro", "basic-plan", PlanChangeTiming.Now, -2700, 0, 0, 2700, 2900, null);
        _mockBillingClient.PreviewPlanChangeAsync(1, "eshop-pro", "basic-plan", PlanChangeTiming.Now, default).Returns(confirmedPreview);

        var updated = _builder.WithState(1, SubscriptionState.Active, productHandle: "basic-plan");
        _mockBillingClient.CommitPlanChangeAsync(1, "basic-plan", PlanChangeTiming.Now, default).Returns(updated);

        var service = CreateService();

        var result = await service.CommitPlanChangeAsync(1, _builder.TestOwnerReference, confirmedPreview);

        Assert.Equal("basic-plan", result.ProductHandle);
        await _mockPublisher.Received().Publish(
            Arg.Is<SubscriptionPlanChanged>(n => n.SubscriptionId == 1 && n.PreviousProductHandle == "eshop-pro" && n.NewProductHandle == "basic-plan"),
            default);
    }
}
