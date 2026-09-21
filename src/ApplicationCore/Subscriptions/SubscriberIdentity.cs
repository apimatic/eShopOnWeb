using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user a subscription belongs to, taken from the caller's authenticated identity.
/// <see cref="UserReference"/> is stable per user and is used as the Maxio customer <c>reference</c>,
/// which the billing provider enforces as unique — the anchor for idempotent customer creation.
/// </summary>
public sealed class SubscriberIdentity
{
    public SubscriberIdentity(string userReference, string email, string? firstName = null, string? lastName = null)
    {
        UserReference = Guard.Against.NullOrWhiteSpace(userReference, nameof(userReference));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable identifier for this user in eShopOnWeb (the JWT name/username). Used as the Maxio customer reference.</summary>
    public string UserReference { get; }

    /// <summary>The user's email address.</summary>
    public string Email { get; }

    /// <summary>Optional given name; the billing service derives a sensible value when absent.</summary>
    public string? FirstName { get; }

    /// <summary>Optional family name; the billing service derives a sensible value when absent.</summary>
    public string? LastName { get; }
}
