namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingUser(
    string Id,
    string Email,
    string FirstName,
    string LastName);
