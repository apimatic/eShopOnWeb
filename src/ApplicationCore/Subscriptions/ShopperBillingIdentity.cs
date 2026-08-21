namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The authenticated eShopOnWeb shopper that Maxio customers are keyed to.
/// </summary>
public sealed record ShopperBillingIdentity(string UserId, string Email, string? UserName);
