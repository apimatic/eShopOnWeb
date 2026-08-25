namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingCustomer(
    string UserId,
    string Email,
    string FirstName,
    string LastName);
