using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string? Name { get; set; }
    public decimal Price { get; set; }
    public string? IntervalUnit { get; set; }
    public int? Interval { get; set; }

    public static PlanDto FromModel(BillingPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Price = plan.Price,
        IntervalUnit = plan.IntervalUnit,
        Interval = plan.Interval
    };
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? ProductHandle { get; set; }
    public decimal? Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }
    public string? NextProductHandle { get; set; }

    public static SubscriptionDto FromModel(BillingSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        Price = subscription.Price,
        State = subscription.State.ToString(),
        NextAssessmentAt = subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        DelayedCancelAt = subscription.DelayedCancelAt,
        NextProductHandle = subscription.NextProductHandle
    };
}

public class PlanChangePreviewDto
{
    public bool ApplyNow { get; set; }
    public long? ProratedAdjustmentInCents { get; set; }
    public long? ChargeInCents { get; set; }
    public long? PaymentDueInCents { get; set; }
    public long? CreditAppliedInCents { get; set; }
    public long TargetPriceInCents { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }
    public string? Note { get; set; }

    public static PlanChangePreviewDto FromModel(PlanChangePreview preview) => new()
    {
        ApplyNow = preview.ApplyNow,
        ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
        ChargeInCents = preview.ChargeInCents,
        PaymentDueInCents = preview.PaymentDueInCents,
        CreditAppliedInCents = preview.CreditAppliedInCents,
        TargetPriceInCents = preview.TargetPriceInCents,
        EffectiveAt = preview.EffectiveAt,
        Note = preview.Note
    };

    public PlanChangePreview ToModel() => new(
        ApplyNow,
        ProratedAdjustmentInCents,
        ChargeInCents,
        PaymentDueInCents,
        CreditAppliedInCents,
        TargetPriceInCents,
        null,
        null);
}
