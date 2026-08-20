namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record CreateBillingCustomer(
    string Reference,
    string Email,
    string FirstName,
    string LastName);
