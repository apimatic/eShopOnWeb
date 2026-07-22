using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The provider-reported subscription states the integration reasons about, and the
/// transitions each lifecycle action is legal from (UC4).
/// </summary>
public static class SubscriptionStates
{
    public const string Active = "active";
    public const string Trialing = "trialing";
    public const string PastDue = "past_due";
    public const string OnHold = "on_hold";
    public const string Canceled = "canceled";
    public const string Expired = "expired";
    public const string TrialEnded = "trial_ended";
    public const string Unpaid = "unpaid";

    private static readonly string[] LiveStates = { Active, Trialing };

    private static readonly IReadOnlyDictionary<SubscriptionLifecycleAction, string[]> LegalFromStates =
        new Dictionary<SubscriptionLifecycleAction, string[]>
        {
            [SubscriptionLifecycleAction.Pause] = new[] { Active, Trialing },
            [SubscriptionLifecycleAction.Resume] = new[] { OnHold },
            [SubscriptionLifecycleAction.Cancel] = new[] { Active, Trialing, PastDue, OnHold },
            [SubscriptionLifecycleAction.Reactivate] = new[] { Canceled, Expired, TrialEnded, Unpaid }
        };

    /// <summary>A subscription that can accrue usage and be moved between plans.</summary>
    public static bool IsLive(string? state) =>
        state is not null && LiveStates.Contains(state, StringComparer.OrdinalIgnoreCase);

    public static bool IsTransitionLegal(SubscriptionLifecycleAction action, string? fromState) =>
        fromState is not null && LegalFromStates[action].Contains(fromState, StringComparer.OrdinalIgnoreCase);

    /// <summary>The actions that are legal from the supplied state, for error messages.</summary>
    public static IReadOnlyCollection<SubscriptionLifecycleAction> LegalTransitionsFrom(string? state) =>
        LegalFromStates.Where(pair => IsTransitionLegal(pair.Key, state))
            .Select(pair => pair.Key)
            .ToArray();
}
