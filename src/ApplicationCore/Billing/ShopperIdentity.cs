namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The authenticated eShopOnWeb shopper to map onto a Maxio customer.
/// </summary>
public sealed record ShopperIdentity(string UserId, string Email, string UserName);
