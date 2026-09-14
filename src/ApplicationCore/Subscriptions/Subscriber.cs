using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper that a billing subscription belongs to.
/// This is the application's identity; the billing system keeps its own customer record keyed by
/// <see cref="Reference"/> so that the two can be correlated idempotently.
/// </summary>
public class Subscriber
{
    public Subscriber(string userId, string email, string? firstName = null, string? lastName = null)
    {
        UserId = Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Identity provider user id (unique within an eShopOnWeb deployment).</summary>
    public string UserId { get; }

    /// <summary>The shopper's e-mail address, also their eShopOnWeb user name.</summary>
    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }

    /// <summary>
    /// Stable, deterministic key used as the billing customer's "Reference (Your App)" value.
    /// The e-mail address is used rather than the identity primary key because it survives an
    /// identity store rebuild (eShopOnWeb can run against an in-memory identity database), which
    /// keeps "ensure the customer exists" idempotent across restarts.
    /// </summary>
    public string Reference => $"eshoponweb:{Email.Trim().ToLowerInvariant()}";
}
