using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewDto
{
    public int SubscriptionId { get; set; }
    public string CurrentProductHandle { get; set; } = string.Empty;
    public string TargetProductHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;
    public long ComparableAmountInCents { get; set; }
    public long? ProratedAdjustmentInCents { get; set; }
    public long? ChargeInCents { get; set; }
    public long? CreditAppliedInCents { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }

    public static PlanChangePreviewDto FromDomain(PlanChangePreview preview) => new()
    {
        SubscriptionId = preview.SubscriptionId,
        CurrentProductHandle = preview.CurrentProductHandle,
        TargetProductHandle = preview.TargetProductHandle,
        Timing = preview.Timing.ToString(),
        ComparableAmountInCents = preview.ComparableAmountInCents,
        ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
        ChargeInCents = preview.ChargeInCents,
        CreditAppliedInCents = preview.CreditAppliedInCents,
        EffectiveAt = preview.EffectiveAt
    };
}
