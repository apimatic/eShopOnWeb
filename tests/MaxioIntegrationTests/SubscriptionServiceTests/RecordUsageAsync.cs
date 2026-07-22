using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class RecordUsageAsync
{
    private readonly SubscriptionServiceFixture _fixture = new();

    private void ArrangeActiveSubscription()
    {
        _fixture.BillingClient.FindCustomerByReferenceAsync(SubscriptionServiceFixture.UserReference,
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.Customer());
        _fixture.BillingClient.ListSubscriptionsAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Active) });
    }

    private void ArrangeMeteredComponent()
    {
        _fixture.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.MeteredComponent());
    }

    private static UsageRecord Recorded(decimal quantity) =>
        new(778899, 90210, 3062734, "api-call", quantity, "eShopOnWeb order 42", DateTimeOffset.UtcNow);

    [Fact]
    public async Task RecordsTheUsageAndReportsTheRunningPeriodTotal()
    {
        ArrangeActiveSubscription();
        ArrangeMeteredComponent();
        _fixture.BillingClient.RecordUsageAsync(90210, Arg.Any<BillingComponent>(), 3m, Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Recorded(3m));
        _fixture.BillingClient.GetPeriodToDateUnitsAsync(90210, Arg.Any<BillingComponent>(),
                Arg.Any<CancellationToken>())
            .Returns(120);

        var report = await _fixture.CreateService()
            .RecordUsageAsync(SubscriptionServiceFixture.UserReference, 3m, "eShopOnWeb order 42");

        Assert.Equal(3m, report.Record.Quantity);
        Assert.Equal(120, report.PeriodToDateUnits);
        // 120 units at $0.01 each is $1.20 on the next renewal invoice.
        Assert.Equal(1.20m, report.PeriodToDateCharge);
    }

    [Fact]
    public async Task KeepsTheUsageWhenTheRunningTotalCannotBeReadBack()
    {
        ArrangeActiveSubscription();
        ArrangeMeteredComponent();
        _fixture.BillingClient.RecordUsageAsync(90210, Arg.Any<BillingComponent>(), 1m, Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Recorded(1m));
        _fixture.BillingClient.GetPeriodToDateUnitsAsync(90210, Arg.Any<BillingComponent>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException(503, "Service unavailable."));

        var report = await _fixture.CreateService()
            .RecordUsageAsync(SubscriptionServiceFixture.UserReference, 1m, null);

        // The usage stands; only the total is reported as unavailable.
        Assert.Equal(1m, report.Record.Quantity);
        Assert.False(report.PeriodToDateUnitsAvailable);
        Assert.Null(report.PeriodToDateCharge);
    }

    [Fact]
    public async Task RejectsUsageForAUserWithNoSubscriptionWithoutCallingTheProvider()
    {
        _fixture.BillingClient.FindCustomerByReferenceAsync(SubscriptionServiceFixture.UserReference,
                Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        await Assert.ThrowsAsync<SubscriptionNotFoundException>(
            () => _fixture.CreateService().RecordUsageAsync(SubscriptionServiceFixture.UserReference, 1m, null));

        await _fixture.BillingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(),
            Arg.Any<BillingComponent>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectsUsageWhenTheSubscriptionIsNotLive()
    {
        _fixture.BillingClient.FindCustomerByReferenceAsync(SubscriptionServiceFixture.UserReference,
                Arg.Any<CancellationToken>())
            .Returns(SubscriptionServiceFixture.Customer());
        _fixture.BillingClient.ListSubscriptionsAsync(Arg.Any<BillingCustomer>(), Arg.Any<CancellationToken>())
            .Returns(new[] { SubscriptionServiceFixture.SubscriptionIn(SubscriptionState.Paused) });

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(
            () => _fixture.CreateService().RecordUsageAsync(SubscriptionServiceFixture.UserReference, 1m, null));

        await _fixture.BillingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(),
            Arg.Any<BillingComponent>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task RejectsANonPositiveQuantityBeforeAnyProviderCall(int quantity)
    {
        ArrangeActiveSubscription();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _fixture.CreateService()
                .RecordUsageAsync(SubscriptionServiceFixture.UserReference, quantity, null));

        await _fixture.BillingClient.DidNotReceive().RecordUsageAsync(Arg.Any<int>(),
            Arg.Any<BillingComponent>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefusesToMeterWhenTheConfiguredComponentIsNotMetered()
    {
        ArrangeActiveSubscription();
        _fixture.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(new BillingComponent(1, "api-call", "Seats", BillingComponentKind.QuantityBased,
                5m, "eshop-subscribe"));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _fixture.CreateService().RecordUsageAsync(SubscriptionServiceFixture.UserReference, 1m, null));

        Assert.Contains("archive it and recreate it as metered", exception.Message);
    }

    [Fact]
    public async Task RefusesToMeterWhenTheComponentBelongsToAnotherProductFamily()
    {
        ArrangeActiveSubscription();
        _fixture.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns(new BillingComponent(1, "api-call", "API Calls", BillingComponentKind.Metered,
                0.01m, "some-other-family"));

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _fixture.CreateService().RecordUsageAsync(SubscriptionServiceFixture.UserReference, 1m, null));

        Assert.Contains("some-other-family", exception.Message);
    }

    [Fact]
    public async Task RefusesToMeterWhenTheComponentDoesNotExistAtAll()
    {
        ArrangeActiveSubscription();
        _fixture.BillingClient.FindComponentByHandleAsync("api-call", Arg.Any<CancellationToken>())
            .Returns((BillingComponent?)null);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => _fixture.CreateService().RecordUsageAsync(SubscriptionServiceFixture.UserReference, 1m, null));
    }
}
