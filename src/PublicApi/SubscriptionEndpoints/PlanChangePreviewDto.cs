using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewDto
{
    public long SubscriptionId { get; set; }
    public string FromProductHandle { get; set; } = string.Empty;
    public string ToProductHandle { get; set; } = string.Empty;
    public string Timing { get; set; } = string.Empty;
    public long ProratedAdjustmentInCents { get; set; }
    public long ChargeInCents { get; set; }
    public long PaymentDueInCents { get; set; }
    public long CreditAppliedInCents { get; set; }

    public static PlanChangePreviewDto FromModel(PlanChangePreview preview) => new()
    {
        SubscriptionId = preview.SubscriptionId,
        FromProductHandle = preview.FromProductHandle,
        ToProductHandle = preview.ToProductHandle,
        Timing = preview.Timing.ToString(),
        ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
        ChargeInCents = preview.ChargeInCents,
        PaymentDueInCents = preview.PaymentDueInCents,
        CreditAppliedInCents = preview.CreditAppliedInCents
    };
}
