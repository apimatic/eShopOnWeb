namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record ShopperBillingIdentity(
    string UserId,
    string Email,
    string FirstName,
    string LastName);
