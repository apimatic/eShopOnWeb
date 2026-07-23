using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// The legal lifecycle transitions for a subscription, expressed as a pure function of its current state.
/// </summary>
/// <remarks>
/// UC4 requires an illegal transition to be rejected locally, with the current state and the legal
/// alternatives, and with no provider call made. This type is that check. The provider remains the system
/// of record: a transition this policy allows can still be rejected by the provider if the state drifted
/// out of band, and the caller surfaces that conflict.
/// </remarks>
public static class SubscriptionLifecyclePolicy
{
    /// <summary>The lifecycle actions that may be attempted against <paramref name="subscription"/>.</summary>
    public static IReadOnlyList<SubscriptionLifecycleAction> AllowedActions(Subscription subscription)
    {
        var allowed = new List<SubscriptionLifecycleAction>();

        switch (subscription.State)
        {
            case SubscriptionState.Active:
            case SubscriptionState.Trialing:
                allowed.Add(SubscriptionLifecycleAction.Pause);
                allowed.Add(SubscriptionLifecycleAction.Cancel);
                if (!subscription.CancelAtEndOfPeriod)
                {
                    allowed.Add(SubscriptionLifecycleAction.CancelAtEndOfPeriod);
                }

                break;

            case SubscriptionState.Paused:
                allowed.Add(SubscriptionLifecycleAction.Resume);
                allowed.Add(SubscriptionLifecycleAction.Cancel);
                break;

            case SubscriptionState.PastDue:
                allowed.Add(SubscriptionLifecycleAction.Cancel);
                if (!subscription.CancelAtEndOfPeriod)
                {
                    allowed.Add(SubscriptionLifecycleAction.CancelAtEndOfPeriod);
                }

                break;

            case SubscriptionState.Pending:
                allowed.Add(SubscriptionLifecycleAction.Cancel);
                break;

            case SubscriptionState.Canceled:
            case SubscriptionState.Expired:
            case SubscriptionState.TrialEnded:
            case SubscriptionState.Unpaid:
                allowed.Add(SubscriptionLifecycleAction.Reactivate);
                break;

            case SubscriptionState.Suspended:
            case SubscriptionState.Failed:
            case SubscriptionState.Unknown:
            default:
                break;
        }

        return allowed;
    }

    /// <summary>True when <paramref name="action"/> is legal from the subscription's current state.</summary>
    public static bool IsAllowed(Subscription subscription, SubscriptionLifecycleAction action) =>
        AllowedActions(subscription).Contains(action);

    /// <summary>
    /// Throws <see cref="InvalidSubscriptionTransitionException"/> when <paramref name="action"/> is not
    /// legal from the subscription's current state.
    /// </summary>
    public static void EnsureAllowed(Subscription subscription, SubscriptionLifecycleAction action)
    {
        var allowed = AllowedActions(subscription);
        if (!allowed.Contains(action))
        {
            throw new InvalidSubscriptionTransitionException(
                subscription.Id, subscription.State, action, allowed);
        }
    }
}
