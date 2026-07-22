using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Parses the free-text choices carried by subscription requests. Binding these as text and parsing here
/// keeps an unrecognised value a reported validation error instead of a silent bind to the default member.
/// </summary>
internal static class SubscriptionRequestParser
{
    public static SubscriptionLifecycleAction ParseAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new InvalidSubscriptionOperationException(
                "A lifecycle action is required: pause, resume, cancel, cancelAtEndOfPeriod or reactivate.");
        }

        var normalized = action.Trim().Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (!Enum.TryParse<SubscriptionLifecycleAction>(normalized, ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(typeof(SubscriptionLifecycleAction), parsed))
        {
            throw new InvalidSubscriptionOperationException(
                $"'{action}' is not a supported lifecycle action. Use pause, resume, cancel, cancelAtEndOfPeriod or reactivate.");
        }

        return parsed;
    }

    public static PlanChangeTiming ParseTiming(string timing)
    {
        // Applying now with proration is the default when no timing is stated.
        if (string.IsNullOrWhiteSpace(timing))
        {
            return PlanChangeTiming.Immediate;
        }

        var normalized = timing.Trim().Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (!Enum.TryParse<PlanChangeTiming>(normalized, ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(typeof(PlanChangeTiming), parsed))
        {
            throw new InvalidSubscriptionOperationException(
                $"'{timing}' is not a supported plan-change timing. Use immediate or atNextRenewal.");
        }

        return parsed;
    }
}
