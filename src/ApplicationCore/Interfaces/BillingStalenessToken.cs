namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Builds the opaque token used to detect a stale plan-change preview (plan.md UC3). The billing provider
/// exposes no formal version/ETag for this, so both the preview (built in <c>MaxioBillingClient</c>) and
/// the commit-time re-check (in <c>SubscriptionService</c>) must derive the token identically from the
/// subscription's product identity/version — hence this lives in ApplicationCore, shared by both.
/// </summary>
public static class BillingStalenessToken
{
    public static string From(BillingSubscription subscription) =>
        $"{subscription.ProductId}:{subscription.ProductVersionNumber}";
}
