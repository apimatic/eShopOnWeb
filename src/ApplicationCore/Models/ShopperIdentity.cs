namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Identity of the logged-in shopper, resolved from the authenticated principal.
/// </summary>
public sealed record ShopperIdentity(
    string UserId,
    string Email,
    string FirstName,
    string LastName);
