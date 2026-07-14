using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ProrationPreviewDto
{
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool AppliesNow { get; set; }
    public int ProratedAdjustmentInCents { get; set; }
    public int ChargeInCents { get; set; }
    public int PaymentDueInCents { get; set; }
    public int CreditAppliedInCents { get; set; }

    public static ProrationPreviewDto FromEntity(BillingProrationPreview preview) => new()
    {
        TargetProductHandle = preview.TargetProductHandle,
        AppliesNow = preview.AppliesNow,
        ProratedAdjustmentInCents = preview.ProratedAdjustmentInCents,
        ChargeInCents = preview.ChargeInCents,
        PaymentDueInCents = preview.PaymentDueInCents,
        CreditAppliedInCents = preview.CreditAppliedInCents,
    };

    /// <summary>Reconstructs the preview the customer was shown, so the commit can verify freshness.</summary>
    public BillingProrationPreview ToEntity() =>
        new(TargetProductHandle, AppliesNow, ProratedAdjustmentInCents, ChargeInCents, PaymentDueInCents, CreditAppliedInCents);
}
