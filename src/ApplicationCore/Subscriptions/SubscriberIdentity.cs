namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user, resolved from the caller's JWT, in the shape the billing abstraction needs to
/// ensure a customer exists. <see cref="Reference"/> is the stable, app-owned key used to look a customer
/// up idempotently (so a double-click never creates two customers).
/// </summary>
public record SubscriberIdentity
{
    /// <summary>Stable app-owned reference for this user (used as the billing customer reference).</summary>
    public required string Reference { get; init; }

    /// <summary>The user's email address.</summary>
    public required string Email { get; init; }

    /// <summary>The user's first name (best-effort; falls back to a derived value).</summary>
    public required string FirstName { get; init; }

    /// <summary>The user's last name (best-effort; falls back to a derived value).</summary>
    public required string LastName { get; init; }
}
