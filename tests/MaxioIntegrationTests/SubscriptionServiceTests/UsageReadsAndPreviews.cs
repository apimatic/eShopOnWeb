using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class UsageReadsAndPreviews
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService Service => new(_billingClient, _publisher, _logger);

    public UsageReadsAndPreviews()
    {
        _billingClient.MeteredComponentHandle.Returns("api-call");
        _billingClient.GetSubscriptionAsync(101, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Active));
        _billingClient.GetPlanByHandleAsync("basic-plan", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.BasicPlan);
    }

    [Fact]
    public async Task ReadsTheRunningUsageTotalForTheConfiguredMeteredComponent()
    {
        _billingClient.GetPeriodToDateUsageAsync(101, "api-call", Arg.Any<CancellationToken>())
            .Returns(17m);

        var total = await Service.GetPeriodToDateUsageAsync(101, SubscriptionBuilder.BuyerId);

        Assert.Equal(17m, total);
        await _billingClient.Received(1).GetPeriodToDateUsageAsync(101, "api-call",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportsAnUnavailableUsageTotalAsNullRatherThanZero()
    {
        _billingClient.GetPeriodToDateUsageAsync(101, "api-call", Arg.Any<CancellationToken>())
            .Returns((decimal?)null);

        Assert.Null(await Service.GetPeriodToDateUsageAsync(101, SubscriptionBuilder.BuyerId));
    }

    [Fact]
    public async Task RefusesToReadUsageForAnotherCustomersSubscription()
    {
        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => Service.GetPeriodToDateUsageAsync(101, "someone.else@microsoft.com"));

        await _billingClient.DidNotReceive().GetPeriodToDateUsageAsync(Arg.Any<int>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QuotesThePlanChangeThroughTheBillingClient()
    {
        var expected = new PlanChangePreview("basic-plan", PlanChangeTiming.Immediately,
            -1500, 27000, 25500, 1500);
        _billingClient.PreviewPlanChangeAsync(101, "basic-plan", PlanChangeTiming.Immediately,
                Arg.Any<CancellationToken>())
            .Returns(expected);

        var preview = await Service.PreviewPlanChangeAsync(101, SubscriptionBuilder.BuyerId,
            "basic-plan", PlanChangeTiming.Immediately);

        Assert.Equal(255.00m, preview.PaymentDue);
        Assert.Equal(-15.00m, preview.ProratedAdjustment);
    }

    [Fact]
    public async Task RefusesToPreviewAChangeToTheCurrentPlan()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => Service.PreviewPlanChangeAsync(101, SubscriptionBuilder.BuyerId, "eshop-pro",
                PlanChangeTiming.Immediately));

        await _billingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<PlanChangeTiming>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAnEmptyTargetPlanHandle()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => Service.PreviewPlanChangeAsync(101, SubscriptionBuilder.BuyerId, "",
                PlanChangeTiming.Immediately));
    }

    [Fact]
    public async Task RejectsANonPositiveSubscriptionId()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => Service.GetPeriodToDateUsageAsync(0, SubscriptionBuilder.BuyerId));

        await _billingClient.DidNotReceive().GetSubscriptionAsync(Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListsThePlansOfferedByTheBillingProvider()
    {
        _billingClient.ListPlansAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionBuilder.ProPlan, SubscriptionBuilder.BasicPlan });

        var plans = await Service.GetAvailablePlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal(299.00m, plans.First().Price);
    }

    [Fact]
    public async Task ReportsNoActiveSubscriptionWhenEveryOneIsCancelled()
    {
        _billingClient.EnsureCustomerAsync(SubscriptionBuilder.BuyerId, Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(55, SubscriptionBuilder.BuyerId, SubscriptionBuilder.BuyerId, null, null));
        _billingClient.ListSubscriptionsForCustomerAsync(55, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionBuilder.WithState(SubscriptionState.Canceled) });

        Assert.Null(await Service.GetActiveSubscriptionForUserAsync(SubscriptionBuilder.BuyerId));
    }
}
