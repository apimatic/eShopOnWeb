namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A customer record in Maxio. The <see cref="Reference"/> is the stable identifier from
/// eShopOnWeb (the application user id) and is what makes "ensure a customer exists" idempotent:
/// Maxio only allows a single customer per reference value.
/// </summary>
public class MaxioCustomer
{
    public long Id { get; init; }

    public string? Reference { get; init; }

    public string Email { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;
}
