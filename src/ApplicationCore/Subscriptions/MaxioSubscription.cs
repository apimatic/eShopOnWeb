using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A Maxio Subscription, describing one customer's enrollment in one plan.
/// </summary>
public record MaxioSubscription(
    int Id,
    string State,
    int CustomerId,
    int ProductId,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// Subscription states that are not an end-of-life state (canceled, expired, failed_to_create,
    /// trial_ended) - i.e. the customer is already considered enrolled in the plan.
    /// See Subscription-State.yaml in the Maxio spec for the full enum and description of each state.
    /// </summary>
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended", "unpaid"
    };

    public bool IsActiveEnrollment => !EndOfLifeStates.Contains(State);
}
