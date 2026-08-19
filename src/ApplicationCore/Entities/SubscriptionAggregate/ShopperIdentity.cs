namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Identity of a logged-in eShopOnWeb shopper used to map to a Maxio customer.
/// </summary>
public sealed record ShopperIdentity(string UserId, string Email, string UserName);
