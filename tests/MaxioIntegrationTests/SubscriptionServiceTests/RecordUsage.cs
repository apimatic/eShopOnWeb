using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class RecordUsage : SubscriptionServiceFixture
{
    public RecordUsage()
    {
        BillingClient.FindCustomerByReferenceAsync(UserReference, Arg.Any<CancellationToken>()).Returns(Customer());
        BillingClient.ListSubscriptionsForCustomerAsync(33, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription() });
        BillingClient.RecordUsageAsync(42, Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Usage(quantity: callInfo.ArgAt<decimal>(1)));
    }

    [Fact]
    public async Task RecordsUsageOnTheCustomersLiveSubscription()
    {
        BillingClient.GetUsageTotalAsync(42, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(12m);
        BillingClient.GetUsageUnitPriceAsync(Arg.Any<CancellationToken>()).Returns(0.01m);

        var summary = await Service.RecordUsageAsync(UserReference, 5m, "five calls");

        Assert.Equal(5m, summary.Recorded.Quantity);
        Assert.True(summary.IsPeriodTotalAvailable);
        Assert.Equal(12m, summary.PeriodToDateQuantity);
    }

    [Fact]
    public async Task PricesThePeriodToDateTotalAtTheUnitPrice()
    {
        BillingClient.GetUsageTotalAsync(42, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(1234m);
        BillingClient.GetUsageUnitPriceAsync(Arg.Any<CancellationToken>()).Returns(0.01m);

        var summary = await Service.RecordUsageAsync(UserReference, 1m, null);

        // 1234 units at $0.01 is $12.34 — not $1234 and not $0.1234.
        Assert.Equal(12.34m, summary.PeriodToDateAmount);
    }

    [Fact]
    public async Task BoundsTheRunningTotalToTheCurrentBillingPeriod()
    {
        BillingClient.GetUsageTotalAsync(42, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(1m);
        BillingClient.GetUsageUnitPriceAsync(Arg.Any<CancellationToken>()).Returns(0.01m);

        await Service.RecordUsageAsync(UserReference, 1m, null);

        await BillingClient.Received(1).GetUsageTotalAsync(42,
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportsSuccessWithTheTotalMarkedUnavailableWhenTheReadBackFails()
    {
        // The provider has already accepted the usage, so it will be billed. A failure to read the
        // running total must not turn a successful operation into a failed one.
        BillingClient.GetUsageTotalAsync(42, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("ListUsages", 0, "unreachable"));

        var summary = await Service.RecordUsageAsync(UserReference, 5m, null);

        Assert.Equal(5m, summary.Recorded.Quantity);
        Assert.False(summary.IsPeriodTotalAvailable);
        Assert.Null(summary.PeriodToDateQuantity);
        Assert.Null(summary.PeriodToDateAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RejectsANonPositiveQuantityBeforeCallingTheProvider(int quantity)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => Service.RecordUsageAsync(UserReference, quantity, null));

        await BillingClient.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsUsageForAUserWithNoProviderCustomer()
    {
        BillingClient.FindCustomerByReferenceAsync("nobody@example.com", Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        await Assert.ThrowsAsync<NoActiveSubscriptionException>(
            () => Service.RecordUsageAsync("nobody@example.com", 1m, null));

        await BillingClient.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsUsageForACustomerWithNoLiveSubscription()
    {
        BillingClient.ListSubscriptionsForCustomerAsync(33, Arg.Any<CancellationToken>())
            .Returns(new[] { Subscription(state: SubscriptionState.Canceled) });

        await Assert.ThrowsAsync<NoActiveSubscriptionException>(
            () => Service.RecordUsageAsync(UserReference, 1m, null));

        await BillingClient.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminUsageRejectsAnUnknownSubscription()
    {
        BillingClient.GetSubscriptionAsync(999, Arg.Any<CancellationToken>()).Returns((CustomerSubscription?)null);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => Service.RecordUsageForSubscriptionAsync(999, 1m, null));
    }

    [Fact]
    public async Task AdminUsageRejectsASubscriptionThatIsNotLive()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>())
            .Returns(Subscription(state: SubscriptionState.OnHold));

        await Assert.ThrowsAsync<NoActiveSubscriptionException>(
            () => Service.RecordUsageForSubscriptionAsync(42, 1m, null));

        await BillingClient.DidNotReceive().RecordUsageAsync(
            Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminUsageRecordsAgainstAnyLiveSubscription()
    {
        BillingClient.GetSubscriptionAsync(42, Arg.Any<CancellationToken>()).Returns(Subscription());
        BillingClient.GetUsageTotalAsync(42, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>()).Returns(3m);
        BillingClient.GetUsageUnitPriceAsync(Arg.Any<CancellationToken>()).Returns(0.01m);

        var summary = await Service.RecordUsageForSubscriptionAsync(42, 2m, "admin adjustment");

        Assert.Equal(2m, summary.Recorded.Quantity);
        Assert.Equal(3m, summary.PeriodToDateQuantity);
    }
}
