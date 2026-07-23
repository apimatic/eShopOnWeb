using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.ApplicationCore.Services.SubscriptionServiceTests;

public class RecordUsage
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly SubscriptionService _subscriptionService;

    public RecordUsage()
    {
        _subscriptionService = new SubscriptionService(_billingClient, Substitute.For<IPublisher>(),
            Substitute.For<IAppLogger<SubscriptionService>>(),
            new SubscriptionSettings { ProductFamilyHandle = "eshop-subscribe", MeteredComponentHandle = "api-call" });

        _billingClient.GetComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Component());
    }

    [Fact]
    public async Task RecordsTheUsageAndReportsTheRunningPeriodToDateTotal()
    {
        _billingClient.RecordUsageAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "api-call", 3m, "batch",
            Arg.Any<CancellationToken>()).Returns(SubscriptionBuilder.UsageRecord(3m));
        _billingClient.GetUsageBalanceAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "api-call",
            Arg.Any<CancellationToken>()).Returns(1250m);

        var report = await _subscriptionService.RecordUsageForSubscriptionAsync(
            SubscriptionBuilder.TEST_SUBSCRIPTION_ID, 3m, "batch");

        Assert.Equal(3m, report.RecordedUsage.Quantity);
        Assert.Equal(1250m, report.PeriodToDateTotal);
        Assert.True(report.IsPeriodToDateTotalAvailable);
    }

    [Fact]
    public async Task LetsTheUsageStandWhenTheTotalCannotBeReadBack()
    {
        _billingClient.RecordUsageAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "api-call", 1m, null,
            Arg.Any<CancellationToken>()).Returns(SubscriptionBuilder.UsageRecord());
        _billingClient.GetUsageBalanceAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "api-call",
            Arg.Any<CancellationToken>()).ThrowsAsync(new BillingProviderException("read balance", 503, new[] { "down" }));

        var report = await _subscriptionService.RecordUsageForSubscriptionAsync(
            SubscriptionBuilder.TEST_SUBSCRIPTION_ID, 1m, null);

        Assert.NotNull(report.RecordedUsage);
        Assert.Null(report.PeriodToDateTotal);
        Assert.False(report.IsPeriodToDateTotalAvailable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task RejectsANonPositiveQuantityBeforeAnythingReachesTheProvider(int quantity)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _subscriptionService.RecordUsageForSubscriptionAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID,
                quantity, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToRecordUsageWhenTheConfiguredComponentIsNotMetered()
    {
        _billingClient.GetComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Component("quantity_based_component"));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _subscriptionService.RecordUsageForSubscriptionAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, 1m, null));

        Assert.Contains("quantity_based_component", exception.Message);
        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToRecordUsageWhenTheConfiguredComponentDoesNotResolve()
    {
        _billingClient.GetComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns((MeteredComponent?)null);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _subscriptionService.RecordUsageForSubscriptionAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, 1m, null));

        Assert.Contains("api-call", exception.Message);
        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidatesTheComponentOnceAndCachesTheResult()
    {
        _billingClient.RecordUsageAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, "api-call", 1m, null,
            Arg.Any<CancellationToken>()).Returns(SubscriptionBuilder.UsageRecord());

        await _subscriptionService.RecordUsageForSubscriptionAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, 1m, null);
        await _subscriptionService.RecordUsageForSubscriptionAsync(SubscriptionBuilder.TEST_SUBSCRIPTION_ID, 1m, null);

        await _billingClient.Received(1).GetComponentByHandleAsync("api-call", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsUsageForAUserWithNoActiveSubscriptionWithoutTouchingTheProvider()
    {
        _billingClient.FindCustomerByReferenceAsync(SubscriptionBuilder.TEST_USER_REFERENCE, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _billingClient.ListSubscriptionsForCustomerAsync(SubscriptionBuilder.TEST_CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionBuilder.Subscription(SubscriptionState.Canceled) });

        await Assert.ThrowsAsync<NoActiveSubscriptionException>(
            () => _subscriptionService.RecordUsageAsync(SubscriptionBuilder.TEST_USER_REFERENCE, 1m, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordsUsageAgainstTheUsersOwnActiveSubscription()
    {
        _billingClient.FindCustomerByReferenceAsync(SubscriptionBuilder.TEST_USER_REFERENCE, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Customer());
        _billingClient.ListSubscriptionsForCustomerAsync(SubscriptionBuilder.TEST_CUSTOMER_ID, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                SubscriptionBuilder.Subscription(SubscriptionState.Canceled, id: 111),
                SubscriptionBuilder.Subscription(SubscriptionState.Active, id: 222)
            });
        _billingClient.RecordUsageAsync(222, "api-call", 4m, "memo", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.UsageRecord(4m));

        var report = await _subscriptionService.RecordUsageAsync(SubscriptionBuilder.TEST_USER_REFERENCE, 4m, "memo");

        Assert.Equal(4m, report.RecordedUsage.Quantity);
        await _billingClient.Received(1).RecordUsageAsync(222, "api-call", 4m, "memo", Arg.Any<CancellationToken>());
    }
}
