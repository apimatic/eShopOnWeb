namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A Maxio Advanced Billing customer.
/// </summary>
public record MaxioCustomer(
    long CustomerId,
    string Reference,
    string FirstName,
    string LastName,
    string Email);
