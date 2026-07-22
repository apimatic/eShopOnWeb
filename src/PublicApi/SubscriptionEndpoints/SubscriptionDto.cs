using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A customer's enrolment in a plan. Money is in whole currency units.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string UserReference { get; set; }
    public int CustomerId { get; set; }
    public SubscriptionPlanDto Plan { get; set; }

    /// <summary>The normalized lifecycle state, e.g. Active or Paused.</summary>
    public string State { get; set; }

    /// <summary>The billing provider's own state name, for states this integration does not model.</summary>
    public string ProviderState { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public decimal Balance { get; set; }

    /// <summary>The plan this subscription switches to at the next renewal, when one is scheduled.</summary>
    public string PendingPlanHandle { get; set; }

    /// <summary>The lifecycle actions that are legal from the current state.</summary>
    public bool CanPause { get; set; }
    public bool CanResume { get; set; }
    public bool CanCancel { get; set; }
    public bool CanReactivate { get; set; }
    public bool CanChangePlan { get; set; }
    public bool CanRecordUsage { get; set; }
}
