using System;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionDto
{
    public long SubscriptionId { get; set; }

    /// <summary>Maxio subscription state (active, trialing, canceled, ...).</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring amount of the subscribed plan, in integer cents.</summary>
    public long? PriceInCents { get; set; }

    public long? BalanceInCents { get; set; }

    public string? Currency { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>End of the current billing period (next regular renewal date).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto From(SubscriptionDetails details)
    {
        return new SubscriptionDto
        {
            SubscriptionId = details.Id,
            State = details.State,
            PlanHandle = details.PlanHandle,
            PlanName = details.PlanName,
            PriceInCents = details.ProductPriceInCents,
            BalanceInCents = details.BalanceInCents,
            Currency = details.Currency,
            PaymentCollectionMethod = details.PaymentCollectionMethod,
            CancelAtEndOfPeriod = details.CancelAtEndOfPeriod,
            CurrentPeriodStartedAt = details.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = details.CurrentPeriodEndsAt,
            NextAssessmentAt = details.NextAssessmentAt,
            ActivatedAt = details.ActivatedAt,
            CreatedAt = details.CreatedAt
        };
    }
}
