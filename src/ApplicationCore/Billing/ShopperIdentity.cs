namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The authenticated eShopOnWeb shopper, mapped to a Maxio customer via <see cref="UserId"/>.
/// </summary>
public sealed record ShopperIdentity(
    string UserId,
    string Email,
    string FirstName,
    string LastName);
