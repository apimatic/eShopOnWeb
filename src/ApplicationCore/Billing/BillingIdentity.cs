namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingIdentity(
    string UserId,
    string FirstName,
    string LastName,
    string Email);
