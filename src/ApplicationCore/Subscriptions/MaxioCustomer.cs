namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A Maxio Customer, keyed to an eShopOnWeb user via <see cref="Reference"/>.
/// </summary>
public record MaxioCustomer(int Id, string? Reference, string Email);

/// <summary>
/// The attributes needed to create a Maxio Customer for an eShopOnWeb user.
/// </summary>
public record MaxioCreateCustomer(string Reference, string Email, string FirstName, string LastName);
