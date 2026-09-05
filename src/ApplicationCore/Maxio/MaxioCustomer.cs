namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A customer record in Maxio Advanced Billing. <see cref="Reference"/> is the eShopOnWeb
/// ASP.NET Identity user id and is the idempotency key used to find-or-create this customer.
/// </summary>
public record MaxioCustomer(int Id, string? Reference, string Email, string FirstName, string LastName);
