namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

/// <summary>
/// Identity of the signed-in shopper, mapped to a Maxio customer via <see cref="UserId"/>.
/// </summary>
public sealed record ShopperIdentity(string UserId, string Email, string? UserName);
