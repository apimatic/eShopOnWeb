namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The authenticated eShopOnWeb shopper that will be mapped to a Maxio customer.
/// </summary>
public sealed record ShopperIdentity(string UserId, string Email, string? UserName);
