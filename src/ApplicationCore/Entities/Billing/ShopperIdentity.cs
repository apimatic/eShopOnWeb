namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

/// <summary>
/// The authenticated eShopOnWeb shopper, used as the Maxio customer identity.
/// <see cref="Reference"/> is stored as the Maxio customer <c>reference</c> (unique per site).
/// </summary>
public sealed record ShopperIdentity(
    string Reference,
    string Email,
    string FirstName,
    string LastName);
