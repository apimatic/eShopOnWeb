using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.SubscriptionServiceTests;

public class RecordUsage
{
    private readonly IBillingClient _billingClient = Substitute.For<IBillingClient>();
    private readonly SubscriptionService _service;

    public RecordUsage()
    {
        _service = new SubscriptionService(_billingClient, Substitute.For<IPublisher>(),
            new NullAppLogger<SubscriptionService>());

        _billingClient.GetUsageComponentAsync(Arg.Any<CancellationToken>()).Returns(TestData.MeteredComponent);
    }

    [Fact]
    public async Task RecordsUsageAgainstTheUsersActiveSubscriptionAndReadsBackTheTotal()
    {
        ArrangeActiveSubscriptionForUser();
        _billingClient.RecordUsageAsync(Arg.Any<RecordUsageRequest>(), Arg.Any<CancellationToken>()).Returns(999L);
        _billingClient.GetPeriodToDateUnitsAsync(TestData.SubscriptionId, TestData.MeteredComponent.Id, Arg.Any<CancellationToken>())
            .Returns(7);

        var result = await _service.RecordUsageAsync(TestData.BuyerId, 5, "order 42");

        Assert.Equal(999L, result.UsageId);
        Assert.Equal(TestData.SubscriptionId, result.SubscriptionId);
        Assert.Equal("api-call", result.ComponentHandle);
        Assert.Equal(5, result.Quantity);
        Assert.Equal("order 42", result.Memo);
        Assert.Equal(7, result.PeriodToDateUnits);
    }

    /// <summary>Seven units at a cent each is seven cents, not seven dollars.</summary>
    [Fact]
    public async Task EstimatesTheAccruedChargeAtTheComponentsUnitPrice()
    {
        ArrangeActiveSubscriptionForUser();
        _billingClient.RecordUsageAsync(Arg.Any<RecordUsageRequest>(), Arg.Any<CancellationToken>()).Returns(999L);
        _billingClient.GetPeriodToDateUnitsAsync(TestData.SubscriptionId, TestData.MeteredComponent.Id, Arg.Any<CancellationToken>())
            .Returns(7);

        var result = await _service.RecordUsageAsync(TestData.BuyerId, 5);

        Assert.Equal(0.01m, result.UnitPrice);
        Assert.Equal(0.07m, result.PeriodToDateEstimatedCharge);
    }

    [Fact]
    public async Task SendsTheResolvedComponentIdAndQuantityToTheProvider()
    {
        ArrangeActiveSubscriptionForUser();
        _billingClient.RecordUsageAsync(Arg.Any<RecordUsageRequest>(), Arg.Any<CancellationToken>()).Returns(999L);

        await _service.RecordUsageAsync(TestData.BuyerId, 3, "memo");

        await _billingClient.Received(1).RecordUsageAsync(
            Arg.Is<RecordUsageRequest>(r =>
                r.SubscriptionId == TestData.SubscriptionId &&
                r.ComponentId == TestData.MeteredComponent.Id &&
                r.Quantity == 3 &&
                r.Memo == "memo"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The usage is already recorded when the read-back runs, so a failed read-back must leave the
    /// usage standing and simply report the total as unavailable.
    /// </summary>
    [Fact]
    public async Task KeepsTheRecordedUsageWhenTheTotalReadBackFails()
    {
        ArrangeActiveSubscriptionForUser();
        _billingClient.RecordUsageAsync(Arg.Any<RecordUsageRequest>(), Arg.Any<CancellationToken>()).Returns(999L);
        _billingClient.GetPeriodToDateUnitsAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("read-back timed out"));

        var result = await _service.RecordUsageAsync(TestData.BuyerId, 5);

        Assert.Equal(999L, result.UsageId);
        Assert.False(result.PeriodToDateAvailable);
        Assert.Null(result.PeriodToDateEstimatedCharge);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task RejectsAZeroOrNegativeQuantityWithoutSendingAnythingToTheProvider(int quantity)
    {
        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() =>
            _service.RecordUsageAsync(TestData.BuyerId, quantity));

        Assert.Contains("greater than zero", exception.Message);
        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default!, default);
        await _billingClient.DidNotReceiveWithAnyArgs().GetUsageComponentAsync(default);
    }

    [Fact]
    public async Task RejectsUsageForAUserWithNoSubscriptionAtAll()
    {
        _billingClient.FindCustomerByReferenceAsync(TestData.BuyerId, Arg.Any<CancellationToken>())
            .Returns((BillingCustomer?)null);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() =>
            _service.RecordUsageAsync(TestData.BuyerId, 1));

        Assert.Contains("no active subscription", exception.Message);
        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default!, default);
    }

    [Fact]
    public async Task RejectsUsageWhenTheUsersOnlySubscriptionIsNotActive()
    {
        ArrangeSubscriptionsForUser(TestData.Subscription(SubscriptionState.Canceled));

        await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() =>
            _service.RecordUsageAsync(TestData.BuyerId, 1));

        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default!, default);
    }

    /// <summary>
    /// A misconfigured component must stop usage before it is recorded, so nothing is mis-billed.
    /// </summary>
    [Fact]
    public async Task RefusesToRecordWhenTheConfiguredComponentIsNotMetered()
    {
        ArrangeActiveSubscriptionForUser();
        _billingClient.GetUsageComponentAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingConfigurationException("component 'api-call' is of kind 'quantity_based_component', not metered"));

        await Assert.ThrowsAsync<BillingConfigurationException>(() => _service.RecordUsageAsync(TestData.BuyerId, 1));

        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default!, default);
    }

    [Fact]
    public async Task RecordsUsageAgainstASpecificSubscriptionForTheAdminSurface()
    {
        _billingClient.GetSubscriptionAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription());
        _billingClient.RecordUsageAsync(Arg.Any<RecordUsageRequest>(), Arg.Any<CancellationToken>()).Returns(555L);

        var result = await _service.RecordUsageForSubscriptionAsync(TestData.SubscriptionId, 2, "admin adjustment");

        Assert.Equal(555L, result.UsageId);
        Assert.Equal(2, result.Quantity);
    }

    [Fact]
    public async Task RejectsAdminUsageForASubscriptionTheProviderDoesNotKnow()
    {
        _billingClient.GetSubscriptionAsync(424242, Arg.Any<CancellationToken>()).Returns((BillingSubscription?)null);

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() =>
            _service.RecordUsageForSubscriptionAsync(424242, 1));

        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public async Task RejectsAdminUsageForASubscriptionThatCannotAccrue()
    {
        _billingClient.GetSubscriptionAsync(TestData.SubscriptionId, Arg.Any<CancellationToken>())
            .Returns(TestData.Subscription(SubscriptionState.Paused));

        var exception = await Assert.ThrowsAsync<InvalidSubscriptionOperationException>(() =>
            _service.RecordUsageForSubscriptionAsync(TestData.SubscriptionId, 1));

        Assert.Contains("Paused", exception.Message);
        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default!, default);
    }

    [Fact]
    public async Task SummarisesUsageWithoutRecordingAnything()
    {
        _billingClient.GetPeriodToDateUnitsAsync(TestData.SubscriptionId, TestData.MeteredComponent.Id, Arg.Any<CancellationToken>())
            .Returns(12);

        var summary = await _service.GetUsageSummaryAsync(TestData.SubscriptionId);

        Assert.Equal(12, summary.PeriodToDateUnits);
        Assert.Equal(0, summary.Quantity);
        Assert.Equal(0.12m, summary.PeriodToDateEstimatedCharge);
        await _billingClient.DidNotReceiveWithAnyArgs().RecordUsageAsync(default!, default);
    }

    private void ArrangeActiveSubscriptionForUser() => ArrangeSubscriptionsForUser(TestData.Subscription());

    private void ArrangeSubscriptionsForUser(params BillingSubscription[] subscriptions)
    {
        _billingClient.FindCustomerByReferenceAsync(TestData.BuyerId, Arg.Any<CancellationToken>())
            .Returns(TestData.Customer);
        _billingClient.ListSubscriptionsForCustomerAsync(TestData.CustomerId, Arg.Any<CancellationToken>())
            .Returns(subscriptions);
    }
}
