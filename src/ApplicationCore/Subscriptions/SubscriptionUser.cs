namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record SubscriptionUser(
    string UserId,
    string Email,
    string FirstName,
    string LastName);
