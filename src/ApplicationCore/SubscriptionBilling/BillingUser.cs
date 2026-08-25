namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

public sealed record BillingUser(
    string Id,
    string Email,
    string FirstName,
    string LastName);
