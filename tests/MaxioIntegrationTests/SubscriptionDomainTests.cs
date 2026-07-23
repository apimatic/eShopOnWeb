using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The domain rules the provider seam is built on: which lifecycle transitions are legal, and how
/// money is presented to a customer.
/// </summary>
public class SubscriptionDomainTests
{
    private static Subscription SubscriptionIn(SubscriptionState state) => new(
        Id: 1,
        State: state,
        CustomerId: 2,
        CustomerReference: "demouser@microsoft.com",
        PlanId: 3,
        PlanHandle: "eshop-pro",
        PlanName: "Pro Plan",
        PlanPriceInCents: 29900,
        CurrentPeriodStartedAt: null,
        CurrentPeriodEndsAt: null,
        NextAssessmentAt: null,
        CancelAtEndOfPeriod: false,
        CanceledAt: null,
        NextPlanHandle: null);

    [Theory]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Pause, true)]
    [InlineData(SubscriptionState.Trialing, SubscriptionLifecycleAction.Pause, true)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Pause, false)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Pause, false)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Resume, true)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Resume, false)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Cancel, true)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Cancel, true)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Cancel, false)]
    [InlineData(SubscriptionState.Expired, SubscriptionLifecycleAction.Cancel, false)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Reactivate, true)]
    [InlineData(SubscriptionState.Expired, SubscriptionLifecycleAction.Reactivate, true)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Reactivate, false)]
    public void CanTransitionTo_EnforcesTheLegalLifecycleTransitions(
        SubscriptionState state,
        SubscriptionLifecycleAction action,
        bool expected)
    {
        Assert.Equal(expected, SubscriptionIn(state).CanTransitionTo(action));
    }

    [Fact]
    public void AllowedTransitions_ListsExactlyWhatIsLegalFromTheCurrentState()
    {
        Assert.Equal(
            new[] { SubscriptionLifecycleAction.Pause, SubscriptionLifecycleAction.Cancel },
            SubscriptionIn(SubscriptionState.Active).AllowedTransitions);

        Assert.Equal(
            new[] { SubscriptionLifecycleAction.Reactivate },
            SubscriptionIn(SubscriptionState.Canceled).AllowedTransitions);
    }

    [Fact]
    public void AnUnknownProviderStateOffersNoTransitions()
    {
        // A state the integration does not model must not be guessed at.
        Assert.Empty(SubscriptionIn(SubscriptionState.Unknown).AllowedTransitions);
    }

    [Theory]
    [InlineData(SubscriptionState.Active, true)]
    [InlineData(SubscriptionState.Trialing, true)]
    [InlineData(SubscriptionState.PastDue, true)]
    [InlineData(SubscriptionState.Paused, false)]
    [InlineData(SubscriptionState.Canceled, false)]
    public void IsLive_ReflectsWhetherTheSubscriptionIsStillBilling(SubscriptionState state, bool expected)
    {
        Assert.Equal(expected, SubscriptionIn(state).IsLive);
    }

    [Fact]
    public void StalePreviewMessage_ShowsRealCurrencyRatherThanAPlaceholderSymbol()
    {
        var exception = new StalePlanChangePreviewException(
            expectedPaymentDueInCents: 16400,
            actualPaymentDueInCents: 0);

        Assert.Contains("$164.00", exception.Message);
        Assert.Contains("$0.00", exception.Message);

        // The invariant culture's generic currency sign must never reach a customer.
        Assert.DoesNotContain("¤", exception.Message);
    }

    [Fact]
    public void UsageReport_MultipliesUnitsByTheUnitPriceCorrectly()
    {
        var record = new UsageRecord(1, 90001, 3057195, Quantity: 5m, Memo: null, RecordedAt: null);

        var report = new UsageReport(record, PeriodToDateQuantity: 250m, UnitPriceInCents: 1);

        // 250 units at $0.01 is $2.50.
        Assert.Equal(2.50m, report.PeriodToDateCharge);
        Assert.True(report.PeriodToDateAvailable);
    }

    [Fact]
    public void UsageReport_ReportsNoChargeWhenTheRunningTotalCouldNotBeRead()
    {
        var record = new UsageRecord(1, 90001, 3057195, Quantity: 5m, Memo: null, RecordedAt: null);

        var report = new UsageReport(record, PeriodToDateQuantity: null, UnitPriceInCents: 1);

        Assert.False(report.PeriodToDateAvailable);
        Assert.Null(report.PeriodToDateCharge);
    }

    [Fact]
    public void SubscriptionActor_RefusesACustomerWithoutAUserName()
    {
        Assert.Throws<ArgumentException>(() => SubscriptionActor.Customer("  "));
        Assert.Null(SubscriptionActor.Administrator().UserName);
        Assert.True(SubscriptionActor.Administrator().IsAdministrator);
        Assert.False(SubscriptionActor.Customer("a@b.com").IsAdministrator);
    }

    [Theory]
    [InlineData(1, "month", "month")]
    [InlineData(3, "month", "3 months")]
    public void BillingPeriod_ReadsNaturally(int interval, string unit, string expected)
    {
        var plan = new BillingPlan(1, "h", "n", null, 2900, interval, unit, false, null);

        Assert.Equal(expected, plan.BillingPeriod);
        Assert.Equal(29.00m, plan.Price);
    }
}
