namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ChangePlanRequest : BaseRequest
{
    public int SubscriptionId { get; init; }
    public string TargetProductHandle { get; init; } = string.Empty;

    // true = apply now with proration; false = apply at next renewal without proration (UC3 step 1).
    public bool ApplyNow { get; init; }

    // Required when ApplyNow is true: the exact preview the customer confirmed, so a stale
    // preview never gets silently applied at a different amount (§ UC3 failure scenarios).
    public int ProratedAdjustmentInCents { get; init; }
    public int ChargeInCents { get; init; }
    public int PaymentDueInCents { get; init; }
    public int CreditAppliedInCents { get; init; }

    public string UserReference { get; init; } = string.Empty;

    public ChangePlanRequest()
    {
    }

    public ChangePlanRequest(int subscriptionId, string targetProductHandle, bool applyNow,
        int proratedAdjustmentInCents, int chargeInCents, int paymentDueInCents, int creditAppliedInCents, string userReference)
    {
        SubscriptionId = subscriptionId;
        TargetProductHandle = targetProductHandle;
        ApplyNow = applyNow;
        ProratedAdjustmentInCents = proratedAdjustmentInCents;
        ChargeInCents = chargeInCents;
        PaymentDueInCents = paymentDueInCents;
        CreditAppliedInCents = creditAppliedInCents;
        UserReference = userReference;
    }
}
