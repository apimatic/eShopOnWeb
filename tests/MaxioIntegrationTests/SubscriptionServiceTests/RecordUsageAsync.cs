using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class RecordUsageAsync
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _logger = Substitute.For<IAppLogger<SubscriptionService>>();

    private SubscriptionService Service => new(_billingClient, _publisher, _logger);

    public RecordUsageAsync()
    {
        _billingClient.MeteredComponentHandle.Returns("api-call");
        _billingClient.GetSubscriptionAsync(101, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(SubscriptionState.Active));
        _billingClient.GetComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.MeteredApiCall);
        _billingClient.RecordUsageAsync(101, "api-call", Arg.Any<decimal>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => SubscriptionBuilder.Usage(callInfo.ArgAt<decimal>(2)));
        _billingClient.GetPeriodToDateUsageAsync(101, "api-call", Arg.Any<CancellationToken>())
            .Returns(42m);
    }

    [Fact]
    public async Task RecordsTheUsageAndReportsTheRunningPeriodTotal()
    {
        var report = await Service.RecordUsageAsync(101, SubscriptionBuilder.BuyerId, 5, "five calls");

        Assert.Equal(5m, report.Recorded.Quantity);
        Assert.Equal(42m, report.PeriodToDateTotal);
        Assert.Equal(0.01m, report.UnitPrice);
    }

    [Fact]
    public async Task CostsTheAccruedUsageAtTheComponentsUnitPrice()
    {
        var report = await Service.RecordUsageAsync(101, SubscriptionBuilder.BuyerId, 5, null);

        // 42 units at $0.01 is $0.42 — not $42, and not $0.
        Assert.Equal(0.42m, report.EstimatedPeriodToDateCharge);
    }

    [Fact]
    public async Task ReportsNoEstimatedChargeWhenTheRunningTotalIsUnavailable()
    {
        _billingClient.GetPeriodToDateUsageAsync(101, "api-call", Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("read-back failed"));

        var report = await Service.RecordUsageAsync(101, SubscriptionBuilder.BuyerId, 5, null);

        // The usage stands; only the total is unavailable.
        Assert.Equal(5m, report.Recorded.Quantity);
        Assert.Null(report.PeriodToDateTotal);
        Assert.Null(report.EstimatedPeriodToDateCharge);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public async Task RejectsANonPositiveQuantityBeforeAnyProviderCall(decimal quantity)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => Service.RecordUsageAsync(101, SubscriptionBuilder.BuyerId, quantity, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SubscriptionState.Canceled)]
    [InlineData(SubscriptionState.Paused)]
    [InlineData(SubscriptionState.Expired)]
    public async Task RefusesToMeterASubscriptionThatIsNotActive(SubscriptionState state)
    {
        _billingClient.GetSubscriptionAsync(101, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.WithState(state));

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => Service.RecordUsageAsync(101, SubscriptionBuilder.BuyerId, 1, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToMeterWhenTheConfiguredComponentIsNotMetered()
    {
        _billingClient.GetComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.QuantityBasedApiCall);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => Service.RecordUsageAsync(101, SubscriptionBuilder.BuyerId, 1, null));

        Assert.Contains("not of metered kind", exception.Message);
        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToMeterWhenTheConfiguredComponentDoesNotResolve()
    {
        _billingClient.GetComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns((MeteredComponent?)null);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => Service.RecordUsageAsync(101, SubscriptionBuilder.BuyerId, 1, null));

        Assert.Contains("does not resolve", exception.Message);
    }

    [Fact]
    public async Task RefusesToMeterAnUnknownSubscription()
    {
        _billingClient.GetSubscriptionAsync(404, Arg.Any<CancellationToken>())
            .Returns((Subscription?)null);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => Service.RecordUsageAsync(404, SubscriptionBuilder.BuyerId, 1, null));
    }

    [Fact]
    public async Task RefusesToMeterASubscriptionBelongingToAnotherCustomer()
    {
        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => Service.RecordUsageAsync(101, "someone.else@microsoft.com", 1, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LetsAnAdministratorMeterAnySubscription()
    {
        // A null owner reference means the caller is an administrator.
        var report = await Service.RecordUsageAsync(101, null, 3, null);

        Assert.Equal(3m, report.Recorded.Quantity);
    }

    [Fact]
    public async Task SkipsMeteringForAUserWithNoActiveSubscription()
    {
        _billingClient.EnsureCustomerAsync(SubscriptionBuilder.BuyerId, Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(55, SubscriptionBuilder.BuyerId, SubscriptionBuilder.BuyerId, null, null));
        _billingClient.ListSubscriptionsForCustomerAsync(55, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());

        // The order-placed hook must ignore non-subscribers rather than fail.
        var report = await Service.RecordUsageForUserAsync(SubscriptionBuilder.BuyerId, 1, null);

        Assert.Null(report);
        await _billingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MetersTheUsersOwnActiveSubscriptionWhenTheyHaveOne()
    {
        _billingClient.EnsureCustomerAsync(SubscriptionBuilder.BuyerId, Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BillingCustomer(55, SubscriptionBuilder.BuyerId, SubscriptionBuilder.BuyerId, null, null));
        _billingClient.ListSubscriptionsForCustomerAsync(55, Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionBuilder.WithState(SubscriptionState.Active) });

        var report = await Service.RecordUsageForUserAsync(SubscriptionBuilder.BuyerId, 1, "order placed");

        Assert.NotNull(report);
        Assert.Equal(1m, report!.Recorded.Quantity);
    }
}
