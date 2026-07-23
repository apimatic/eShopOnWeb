using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Parses the string-valued enums the API accepts. An unrecognised value is rejected as invalid
/// input rather than silently defaulting, so a typo can never apply the wrong timing or transition.
/// </summary>
public static class PlanChangeTimingParser
{
    public static PlanChangeTiming ParseTiming(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PlanChangeTiming.Immediate;
        }

        if (Enum.TryParse<PlanChangeTiming>(value, ignoreCase: true, out var timing))
        {
            return timing;
        }

        throw new InvalidSubscriptionOperationException(
            $"'{value}' is not a valid plan change timing. Use '{nameof(PlanChangeTiming.Immediate)}' or " +
            $"'{nameof(PlanChangeTiming.AtNextRenewal)}'.");
    }

    public static CancellationTiming ParseCancellationTiming(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CancellationTiming.Immediate;
        }

        if (Enum.TryParse<CancellationTiming>(value, ignoreCase: true, out var timing))
        {
            return timing;
        }

        throw new InvalidSubscriptionOperationException(
            $"'{value}' is not a valid cancellation timing. Use '{nameof(CancellationTiming.Immediate)}' or " +
            $"'{nameof(CancellationTiming.EndOfPeriod)}'.");
    }

    public static SubscriptionLifecycleAction ParseAction(string? value)
    {
        if (Enum.TryParse<SubscriptionLifecycleAction>(value, ignoreCase: true, out var action))
        {
            return action;
        }

        throw new InvalidSubscriptionOperationException(
            $"'{value}' is not a valid lifecycle action. Use " +
            $"'{nameof(SubscriptionLifecycleAction.Pause)}', '{nameof(SubscriptionLifecycleAction.Resume)}', " +
            $"'{nameof(SubscriptionLifecycleAction.Cancel)}' or '{nameof(SubscriptionLifecycleAction.Reactivate)}'.");
    }
}
