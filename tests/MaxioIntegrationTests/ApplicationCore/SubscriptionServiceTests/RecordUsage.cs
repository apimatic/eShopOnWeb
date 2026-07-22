using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.ApplicationCore.SubscriptionServiceTests;

public class RecordUsage
{
    private const string UserName = SubscriptionBuilder.UserName;
    private const string ComponentHandle = MaxioClientBuilder.MeteredComponentHandle;

    private readonly SubscriptionServiceBuilder _builder = new SubscriptionServiceBuilder().WithMeteredComponent();

    public RecordUsage()
    {
        _builder.BillingClient.GetSubscriptionAsync(15236915, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionBuilder().Build());
        _builder.BillingClient.RecordUsageAsync(15236915, ComponentHandle, Arg.Any<decimal>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new UsageRecord(138522957, 15236915, 3062732, ComponentHandle,
                call.ArgAt<decimal>(2), call.ArgAt<string?>(3), DateTimeOffset.UtcNow));
        _builder.BillingClient.GetUsageBalanceAsync(15236915, ComponentHandle, Arg.Any<CancellationToken>())
            .Returns(42m);
    }

    [Fact]
    public async Task RecordsTheUsageAndReturnsTheRunningTotal()
    {
        var report = await _builder.Build().RecordUsageForSubscriptionAsync(15236915, 3, "Order 42 placed");

        Assert.Equal(3, report.Record.Quantity);
        Assert.Equal("Order 42 placed", report.Record.Memo);
        Assert.Equal(42m, report.PeriodToDateBalance);
        Assert.False(report.BalanceUnavailable);
    }

    [Fact]
    public async Task ReportsUsageAgainstTheCallersOwnLiveSubscription()
    {
        _builder.BillingClient.ListSubscriptionsAsync(UserName, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new SubscriptionBuilder().WithId(1).InState(SubscriptionState.Canceled).Build(),
                new SubscriptionBuilder().Build()
            });

        await _builder.Build().RecordUsageAsync(UserName, 1, null);

        // The cancelled subscription must be skipped in favour of the live one.
        await _builder.BillingClient.Received(1).RecordUsageAsync(15236915, ComponentHandle, 1, null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsAUsageReportWhenTheCustomerHasNoLiveSubscription()
    {
        _builder.BillingClient.ListSubscriptionsAsync(UserName, Arg.Any<CancellationToken>())
            .Returns(new[] { new SubscriptionBuilder().InState(SubscriptionState.Canceled).Build() });

        await Assert.ThrowsAsync<NoActiveSubscriptionException>(
            () => _builder.Build().RecordUsageAsync(UserName, 1, null));

        // Nothing is sent to the provider.
        await _builder.BillingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public async Task RejectsAZeroOrNegativeQuantityBeforeAnyProviderCall(decimal quantity)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => _builder.Build().RecordUsageForSubscriptionAsync(15236915, quantity, null));

        await _builder.BillingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToRecordUsageAgainstANonMeteredComponent()
    {
        _builder.BillingClient.FindMeteredComponentAsync(ComponentHandle, Arg.Any<CancellationToken>())
            .Returns(new MeteredComponent(3062733, ComponentHandle, "Seats", "quantity_based_component",
                "seat", "per_unit", 12.50m));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _builder.Build().RecordUsageForSubscriptionAsync(15236915, 1, null));

        Assert.Contains("quantity_based_component", exception.Message);
        await _builder.BillingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(), Arg.Any<string>(),
            Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToRecordUsageWhenTheComponentHandleDoesNotResolve()
    {
        _builder.BillingClient.FindMeteredComponentAsync(ComponentHandle, Arg.Any<CancellationToken>())
            .Returns((MeteredComponent?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _builder.Build().RecordUsageForSubscriptionAsync(15236915, 1, null));
    }

    [Fact]
    public async Task KeepsTheUsageWhenTheRunningTotalCannotBeReadBack()
    {
        _builder.BillingClient.GetUsageBalanceAsync(15236915, ComponentHandle, Arg.Any<CancellationToken>())
            .Throws(new BillingProviderException("GetUsageBalanceAsync", 500, Array.Empty<string>()));

        var report = await _builder.Build().RecordUsageForSubscriptionAsync(15236915, 3, null);

        // The usage stands; only the total is reported as unavailable (UC2 failure scenarios).
        Assert.Equal(3, report.Record.Quantity);
        Assert.Null(report.PeriodToDateBalance);
        Assert.True(report.BalanceUnavailable);
    }

    [Fact]
    public async Task RejectsUsageAgainstAnUnknownSubscriptionId()
    {
        _builder.BillingClient.GetSubscriptionAsync(999999999, Arg.Any<CancellationToken>())
            .Returns((Subscription?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _builder.Build().RecordUsageForSubscriptionAsync(999999999, 1, null));
    }
}
