namespace Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;

/// <summary>
/// Identity of the eShopOnWeb user on whose behalf billing operations are performed.
/// </summary>
/// <param name="UserId">Stable eShopOnWeb user id; used as the Maxio customer reference.</param>
/// <param name="Email">The user's email address.</param>
public record SubscriberInfo(string UserId, string Email);
