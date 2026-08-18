namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Identity fields needed to ensure a Maxio customer for the authenticated shopper.
/// Maxio <c>reference</c> is the shopper email (unique and stable; Identity user ids churn
/// when the host runs against the in-memory database).
/// </summary>
public sealed record ShopperProfile(string UserId, string Email, string? UserName)
{
    public string BillingReference => Email.Trim();
}
