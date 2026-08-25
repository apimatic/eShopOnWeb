namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// Identity of the eShopOnWeb shopper as known to the billing system.
/// <paramref name="UserId"/> is used as the Maxio customer reference.
/// </summary>
public record ShopperInfo(string UserId, string Email, string FirstName, string LastName);
