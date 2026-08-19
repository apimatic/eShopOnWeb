namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The authenticated eShopOnWeb shopper, mapped 1:1 onto a Maxio customer via <see cref="UserId"/>.
/// </summary>
public sealed record ShopperIdentity(string UserId, string Email, string DisplayName);
