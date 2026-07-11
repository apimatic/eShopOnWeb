using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewDto
{
    public int SubscriptionId { get; init; }
    public string CurrentProductHandle { get; init; } = string.Empty;
    public string TargetProductHandle { get; init; } = string.Empty;
    public bool Immediate { get; init; }
    public long? ProratedAdjustmentInCents { get; init; }
    public long? ChargeInCents { get; init; }
    public long? PaymentDueInCents { get; init; }
    public long? CreditAppliedInCents { get; init; }
    public string CommitToken { get; init; } = string.Empty;

    public static PlanChangePreviewDto FromDomain(PlanChangePreview preview) => new()
    {
        SubscriptionId = preview.SubscriptionId,
        CurrentProductHandle = preview.CurrentProductHandle,
        TargetProductHandle = preview.TargetProductHandle,
        Immediate = preview.Immediate,
        ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
        ChargeInCents = preview.ChargeInCents,
        PaymentDueInCents = preview.PaymentDueInCents,
        CreditAppliedInCents = preview.CreditAppliedInCents,
        CommitToken = preview.CommitToken,
    };
}
