namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// The authenticated eShopOnWeb user that maps 1:1 onto a Maxio customer via <see cref="UserId"/>.
/// </summary>
public sealed record ShopperIdentity(string UserId, string Email, string? UserName);
