namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record MaxioCustomer(int Id, string Email, string? Reference);

public sealed record CreateMaxioCustomerRequest(
    string FirstName,
    string LastName,
    string Email,
    string Reference,
    string? Organization);

public sealed record CreateMaxioSubscriptionRequest(
    int CustomerId,
    string ProductHandle,
    string Reference,
    string UniquenessToken);
