using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The domain rules the service leans on: which transitions are legal from which state, and the
/// preview fingerprint that makes a quote binding.
/// </summary>
public class SubscriptionDomainRules
{
    [Theory]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Pause, true)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Cancel, true)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.CancelAtEndOfPeriod, true)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Resume, false)]
    [InlineData(SubscriptionState.Active, SubscriptionLifecycleAction.Reactivate, false)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Resume, true)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Cancel, true)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.Pause, false)]
    [InlineData(SubscriptionState.Paused, SubscriptionLifecycleAction.CancelAtEndOfPeriod, false)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Reactivate, true)]
    [InlineData(SubscriptionState.Canceled, SubscriptionLifecycleAction.Cancel, false)]
    [InlineData(SubscriptionState.Expired, SubscriptionLifecycleAction.Reactivate, true)]
    [InlineData(SubscriptionState.PastDue, SubscriptionLifecycleAction.Cancel, true)]
    [InlineData(SubscriptionState.PastDue, SubscriptionLifecycleAction.CancelAtEndOfPeriod, true)]
    [InlineData(SubscriptionState.Unknown, SubscriptionLifecycleAction.Pause, false)]
    public void LegalTransitionsDependOnTheCurrentState(
        SubscriptionState state,
        SubscriptionLifecycleAction action,
        bool expected)
    {
        var subscription = SubscriptionServiceHarness.Sub(state: state);

        Assert.Equal(expected, subscription.IsActionLegal(action));
        Assert.Equal(expected, subscription.LegalActions.Contains(action));
    }

    [Theory]
    [InlineData(SubscriptionState.Active, true)]
    [InlineData(SubscriptionState.Trialing, true)]
    [InlineData(SubscriptionState.PastDue, false)]
    [InlineData(SubscriptionState.Paused, false)]
    [InlineData(SubscriptionState.Canceled, false)]
    public void OnlyBillingSubscriptionsCountAsActive(SubscriptionState state, bool expected)
    {
        Assert.Equal(expected, SubscriptionServiceHarness.Sub(state: state).IsActive);
    }

    [Theory]
    [InlineData(SubscriptionState.Active, true)]
    [InlineData(SubscriptionState.Trialing, true)]
    [InlineData(SubscriptionState.PastDue, true)]
    [InlineData(SubscriptionState.Canceled, false)]
    [InlineData(SubscriptionState.Expired, false)]
    [InlineData(SubscriptionState.Paused, false)]
    public void PlanChangesAreOnlyOfferedWhereTheyCanBeApplied(SubscriptionState state, bool expected)
    {
        Assert.Equal(expected, SubscriptionServiceHarness.Sub(state: state).CanChangePlan);
    }

    [Fact]
    public void TwoIdenticalQuotesProduceTheSameToken()
    {
        Assert.Equal(SubscriptionServiceHarness.Preview().Token, SubscriptionServiceHarness.Preview().Token);
    }

    [Fact]
    public void AQuoteWithADifferentAmountDueProducesADifferentToken()
    {
        var shown = SubscriptionServiceHarness.Preview(paymentDue: 50.00m);
        var moved = SubscriptionServiceHarness.Preview(paymentDue: 50.01m);

        Assert.NotEqual(shown.Token, moved.Token);
    }

    [Fact]
    public void AQuoteForADifferentTargetPlanProducesADifferentToken()
    {
        var toBasic = SubscriptionServiceHarness.Preview(target: "basic-plan");
        var toPro = SubscriptionServiceHarness.Preview(target: "eshop-pro");

        Assert.NotEqual(toBasic.Token, toPro.Token);
    }

    [Fact]
    public void AQuoteWithDifferentTimingProducesADifferentToken()
    {
        var now = SubscriptionServiceHarness.Preview(timing: PlanChangeTiming.Immediate);
        var atRenewal = SubscriptionServiceHarness.Preview(timing: PlanChangeTiming.NextRenewal);

        Assert.NotEqual(now.Token, atRenewal.Token);
    }

    [Fact]
    public void AQuoteForADifferentSubscriptionProducesADifferentToken()
    {
        Assert.NotEqual(
            SubscriptionServiceHarness.Preview(subscriptionId: 88001).Token,
            SubscriptionServiceHarness.Preview(subscriptionId: 88002).Token);
    }

    [Theory]
    [InlineData(1, "month", "month")]
    [InlineData(3, "month", "3 months")]
    [InlineData(1, "day", "day")]
    [InlineData(14, "day", "14 days")]
    public void BillingCadenceReadsNaturally(int interval, string unit, string expected)
    {
        var plan = SubscriptionServiceHarness.Plan() with { Interval = interval, IntervalUnit = unit };

        Assert.Equal(expected, plan.BillingPeriodDescription);
    }

    [Fact]
    public void AUsageReceiptWithoutARunningTotalReportsItAsUnavailable()
    {
        var receipt = new UsageReceipt { Recorded = SubscriptionServiceHarness.Usage(), PeriodToDateUnits = null };

        Assert.False(receipt.PeriodToDateAvailable);
    }

    [Fact]
    public void AUsageReceiptReportingZeroUnitsIsStillAvailable()
    {
        // Zero used is a real answer and must not be confused with "could not read".
        var receipt = new UsageReceipt { Recorded = SubscriptionServiceHarness.Usage(), PeriodToDateUnits = 0 };

        Assert.True(receipt.PeriodToDateAvailable);
    }
}
