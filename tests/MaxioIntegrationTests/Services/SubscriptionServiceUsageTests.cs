using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Services;

/// <summary>UC2 — pay-as-you-go usage billing.</summary>
public class SubscriptionServiceUsageTests
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly SubscriptionService _service;

    public SubscriptionServiceUsageTests()
    {
        _service = new SubscriptionService(
            _billingClient, Substitute.For<IPublisher>(), Substitute.For<IAppLogger<SubscriptionService>>());

        _billingClient.GetMeteredComponentAsync(Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.MeteredComponent());
        _billingClient.GetSubscriptionAsync(100, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription());
    }

    [Fact]
    public async Task RecordsUsageAndReportsTheRunningPeriodToDateTotal()
    {
        _billingClient.RecordUsageAsync(100, 5m, "nightly batch", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.UsageRecord(5m));
        _billingClient.GetPeriodToDateUsageAsync(100, Arg.Any<CancellationToken>()).Returns(42);

        var report = await _service.RecordUsageAsync(100, SubscriptionBuilder.UserReference, 5m, "nightly batch");

        Assert.Equal(5m, report.Record.Quantity);
        Assert.True(report.PeriodToDateAvailable);
        Assert.Equal(42, report.PeriodToDateUnits);
        Assert.Equal(0.01m, report.UnitPrice);

        // 42 units at $0.01 each is $0.42 — not $42 and not 42 cents-of-a-cent.
        Assert.Equal(0.42m, report.PeriodToDateAmount);
    }

    [Fact]
    public async Task ReportsSuccessWithTheTotalUnavailableWhenTheReadBackFails()
    {
        _billingClient.RecordUsageAsync(100, 1m, null, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.UsageRecord());
        _billingClient.GetPeriodToDateUsageAsync(100, Arg.Any<CancellationToken>())
            .Throws(new BillingProviderException("read-back timed out", 504));

        var report = await _service.RecordUsageAsync(100, SubscriptionBuilder.UserReference, 1m, null);

        Assert.False(report.PeriodToDateAvailable);
        Assert.Null(report.PeriodToDateAmount);
        Assert.Equal(1m, report.Record.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public async Task RejectsANonPositiveQuantityBeforeAnyProviderCall(decimal quantity)
    {
        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(100, SubscriptionBuilder.UserReference, quantity, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToRecordUsageAgainstASubscriptionThatIsNotActive()
    {
        _billingClient.GetSubscriptionAsync(101, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(id: 101, state: SubscriptionState.Canceled));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(101, SubscriptionBuilder.UserReference, 1m, null));

        Assert.Contains("Canceled", exception.Message);
        await _billingClient.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToRecordUsageWhenTheConfiguredComponentIsNotMetered()
    {
        _billingClient.GetMeteredComponentAsync(Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.MeteredComponent(isMetered: false));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _service.RecordUsageAsync(100, SubscriptionBuilder.UserReference, 1m, null));

        Assert.Contains("quantity_based_component", exception.Message);
        await _billingClient.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsUsageForAUserWithNoActiveSubscription()
    {
        _billingClient.ListSubscriptionsAsync("nobody@microsoft.com", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Subscription>());

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageForUserAsync("nobody@microsoft.com", 1m, null));

        Assert.Contains("no active subscription", exception.Message);
    }

    [Fact]
    public async Task RecordsUsageForAUserAgainstTheirActiveSubscription()
    {
        _billingClient.ListSubscriptionsAsync(SubscriptionBuilder.UserReference, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                SubscriptionBuilder.Subscription(id: 98, state: SubscriptionState.Canceled),
                SubscriptionBuilder.Subscription(id: 100)
            });
        _billingClient.RecordUsageAsync(100, 1m, "order 7", Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.UsageRecord());
        _billingClient.GetPeriodToDateUsageAsync(100, Arg.Any<CancellationToken>()).Returns(3);

        var report = await _service.RecordUsageForUserAsync(SubscriptionBuilder.UserReference, 1m, "order 7");

        Assert.Equal(100, report.Record.SubscriptionId);
        Assert.Equal(3, report.PeriodToDateUnits);
        Assert.Equal(0.03m, report.PeriodToDateAmount);
    }

    [Fact]
    public async Task RefusesToRecordUsageOnSomebodyElsesSubscription()
    {
        _billingClient.GetSubscriptionAsync(200, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(id: 200, customerReference: "someone.else@microsoft.com"));

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(200, SubscriptionBuilder.UserReference, 1m, null));

        await _billingClient.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AllowsAnAdministrativeCallerToRecordUsageOnAnySubscription()
    {
        _billingClient.GetSubscriptionAsync(200, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.Subscription(id: 200, customerReference: "someone.else@microsoft.com"));
        _billingClient.RecordUsageAsync(200, 2m, null, Arg.Any<CancellationToken>())
            .Returns(SubscriptionBuilder.UsageRecord(2m, subscriptionId: 200));
        _billingClient.GetPeriodToDateUsageAsync(200, Arg.Any<CancellationToken>()).Returns(2);

        var report = await _service.RecordUsageAsync(200, ownerReference: null, quantity: 2m, memo: null);

        Assert.Equal(200, report.Record.SubscriptionId);
    }

    [Fact]
    public async Task RejectsUsageForAnUnknownSubscriptionId()
    {
        _billingClient.GetSubscriptionAsync(9_999, Arg.Any<CancellationToken>()).Returns((Subscription?)null);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(
            () => _service.RecordUsageAsync(9_999, SubscriptionBuilder.UserReference, 1m, null));

        Assert.Contains("9999", exception.Message);
    }

    [Fact]
    public async Task SummarisesUsageWithoutRecordingAnything()
    {
        _billingClient.GetPeriodToDateUsageAsync(100, Arg.Any<CancellationToken>()).Returns(7);

        var report = await _service.GetUsageSummaryAsync(100, SubscriptionBuilder.UserReference);

        Assert.NotNull(report);
        Assert.Equal(7, report.PeriodToDateUnits);
        Assert.Equal(0.07m, report.PeriodToDateAmount);
        await _billingClient.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsNoUsageSummaryForAnUnknownSubscription()
    {
        _billingClient.GetSubscriptionAsync(9_999, Arg.Any<CancellationToken>()).Returns((Subscription?)null);

        Assert.Null(await _service.GetUsageSummaryAsync(9_999, SubscriptionBuilder.UserReference));
    }
}
