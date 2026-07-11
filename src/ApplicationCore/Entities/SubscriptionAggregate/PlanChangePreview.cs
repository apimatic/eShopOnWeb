namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The previewed cost of moving a subscription to a different plan (UC3), shown to the customer before
/// they confirm. <see cref="CommitToken"/> must be echoed back unchanged on commit so the service can
/// detect a stale preview (the pricing basis changed between preview and confirm) and refuse to apply a
/// different amount than the one shown.
/// </summary>
public class PlanChangePreview
{
    public PlanChangePreview(
        int subscriptionId,
        string currentProductHandle,
        string targetProductHandle,
        bool immediate,
        long? proratedAdjustmentInCents,
        long? chargeInCents,
        long? paymentDueInCents,
        long? creditAppliedInCents,
        string commitToken)
    {
        SubscriptionId = subscriptionId;
        CurrentProductHandle = currentProductHandle;
        TargetProductHandle = targetProductHandle;
        Immediate = immediate;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
        CommitToken = commitToken;
    }

    public int SubscriptionId { get; }
    public string CurrentProductHandle { get; }
    public string TargetProductHandle { get; }
    public bool Immediate { get; }
    public long? ProratedAdjustmentInCents { get; }
    public long? ChargeInCents { get; }
    public long? PaymentDueInCents { get; }
    public long? CreditAppliedInCents { get; }

    /// <summary>Opaque token binding a commit request to the exact amounts previewed.</summary>
    public string CommitToken { get; }
}
