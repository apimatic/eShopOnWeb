namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// Identity of an eShopOnWeb shopper used to provision a Maxio customer.
/// </summary>
public sealed record ShopperIdentity(
    string UserId,
    string Email,
    string FirstName,
    string LastName);
