namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The authenticated eShopOnWeb shopper, used as the Maxio customer reference.
/// </summary>
public sealed record ShopperIdentity(string UserId, string Email, string? UserName);
