using System;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Parses the enumerations carried on the wire. Values are matched regardless of case and
/// separators, and the common spellings of each concept are accepted; anything else is a caller
/// mistake and is reported with the values that would have worked.
/// </summary>
public static class SubscriptionRequestParser
{
    /// <summary>Property names accepted for the plan a request targets.</summary>
    public static readonly string[] PlanNames =
    {
        "planHandle", "productHandle", "targetPlanHandle", "targetProductHandle", "newPlanHandle",
        "newProductHandle", "toPlanHandle", "nextProductHandle", "subscriptionPlanHandle",
        "planIdentifier", "productIdentifier", "planCode", "productCode", "planId", "productId",
        "targetPlanId", "targetProductId", "newPlanId", "newProductId", "targetPlan", "targetProduct",
        "newPlan", "newProduct", "toPlan", "toProduct", "plan", "product", "target", "to", "handle"
    };

    /// <summary>Property names accepted for when a change takes effect.</summary>
    public static readonly string[] TimingNames =
    {
        "timing", "when", "changeTiming", "planChangeTiming", "cancellationTiming", "effective", "schedule"
    };

    /// <summary>Property names accepted for the previously quoted amount, in major units.</summary>
    public static readonly string[] PreviewedPaymentDueNames =
    {
        "previewedPaymentDue", "confirmedAmountDue", "paymentDue", "amountDue", "previewPaymentDue",
        "expectedPaymentDue", "quotedPaymentDue"
    };

    /// <summary>Property names accepted for the previously quoted amount, in minor units.</summary>
    public static readonly string[] PreviewedPaymentDueInCentsNames =
    {
        "confirmedAmountDueInCents", "previewedPaymentDueInCents", "amountDueInCents", "paymentDueInCents",
        "expectedAmountDueInCents", "quotedAmountDueInCents"
    };

    /// <summary>Property names accepted for a usage quantity.</summary>
    public static readonly string[] QuantityNames = { "quantity", "units", "qty", "amount", "usage", "unitCount" };

    /// <summary>Property names accepted for a free-text note.</summary>
    public static readonly string[] MemoNames = { "memo", "note", "description", "comment" };

    /// <summary>Property names accepted for a lifecycle action.</summary>
    public static readonly string[] ActionNames = { "action", "operation", "command", "lifecycleAction", "transition" };

    /// <summary>Property names accepted for a cancellation reason.</summary>
    public static readonly string[] ReasonNames = { "reason", "cancellationMessage", "message", "note", "comment" };

    /// <summary>Property names accepted for the boolean form of "at the end of the period".</summary>
    public static readonly string[] EndOfPeriodNames = { "cancelAtEndOfPeriod", "atEndOfPeriod", "endOfPeriod", "delayed" };

    /// <summary>Property names accepted for the boolean form of "prorate this change".</summary>
    public static readonly string[] ProrationNames =
    {
        "applyNow", "applyImmediately", "prorate", "proration", "prorated", "immediate", "now"
    };

    public static PlanChangeTiming ParsePlanChangeTiming(string? value, bool? prorate = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return prorate == false ? PlanChangeTiming.NextRenewal : PlanChangeTiming.Immediate;
        }

        return Normalize(value) switch
        {
            "immediate" or "immediately" or "now" or "instant" or "prorated" or "proration" or "atonce"
                => PlanChangeTiming.Immediate,
            "nextrenewal" or "renewal" or "atrenewal" or "delayed" or "deferred" or "endofperiod"
                or "nextbillingcycle" or "nextperiod" or "nextbillingperiod" or "periodend"
                => PlanChangeTiming.NextRenewal,
            _ => throw Invalid(value, nameof(PlanChangeTiming), Enum.GetNames<PlanChangeTiming>())
        };
    }

    public static SubscriptionCancellationTiming ParseCancellationTiming(string? value, bool? endOfPeriod = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return endOfPeriod == true
                ? SubscriptionCancellationTiming.EndOfPeriod
                : SubscriptionCancellationTiming.Immediate;
        }

        return Normalize(value) switch
        {
            "immediate" or "immediately" or "now" or "instant" or "atonce"
                => SubscriptionCancellationTiming.Immediate,
            "endofperiod" or "atendofperiod" or "periodend" or "atperiodend" or "delayed" or "deferred"
                or "nextrenewal" or "renewal" or "endofbillingperiod"
                => SubscriptionCancellationTiming.EndOfPeriod,
            _ => throw Invalid(value, nameof(SubscriptionCancellationTiming),
                Enum.GetNames<SubscriptionCancellationTiming>())
        };
    }

    public static SubscriptionLifecycleAction ParseLifecycleAction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidBillingRequestException(
                $"A lifecycle action is required. Accepted values: {string.Join(", ", Enum.GetNames<SubscriptionLifecycleAction>())}.");
        }

        return Normalize(value) switch
        {
            "pause" or "paused" or "hold" or "onhold" or "suspend" => SubscriptionLifecycleAction.Pause,
            "resume" or "resumed" or "unpause" or "unhold" or "release" => SubscriptionLifecycleAction.Resume,
            "cancel" or "canceled" or "cancelled" or "cancellation" or "terminate" => SubscriptionLifecycleAction.Cancel,
            "reactivate" or "reactivated" or "reactivation" or "restore" or "reinstate"
                => SubscriptionLifecycleAction.Reactivate,
            _ => throw Invalid(value, nameof(SubscriptionLifecycleAction), Enum.GetNames<SubscriptionLifecycleAction>())
        };
    }

    private static InvalidBillingRequestException Invalid(string value, string name, string[] accepted)
        => new($"'{value}' is not a valid {name}. Accepted values: {string.Join(", ", accepted)}.");

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}
