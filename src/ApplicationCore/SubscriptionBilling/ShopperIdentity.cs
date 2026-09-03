namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// The signed-in eShopOnWeb user as needed to create or look up a Maxio customer.
/// </summary>
public sealed record ShopperIdentity(
    string UserId,
    string Email,
    string FirstName,
    string LastName);
