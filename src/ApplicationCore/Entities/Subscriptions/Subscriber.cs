namespace Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb user that is (or will become) a customer in the
/// Maxio billing system. <see cref="Reference"/> is the stable key that maps a
/// local user to a single Maxio customer; it is what keeps customer creation
/// idempotent.
/// </summary>
public record Subscriber
{
    /// <summary>
    /// Stable, unique identifier for this user carried over into Maxio as the
    /// customer <c>reference</c>. Two calls with the same reference always resolve
    /// to the same Maxio customer.
    /// </summary>
    public required string Reference { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }
}
