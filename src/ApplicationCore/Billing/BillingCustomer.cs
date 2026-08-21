namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingCustomer(
    int Id,
    string Reference,
    string? FirstName,
    string? LastName,
    string? Email);

public sealed record CreateBillingCustomer(
    string Reference,
    string FirstName,
    string LastName,
    string Email);
