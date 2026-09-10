using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb user being enrolled, derived from the authenticated caller's
/// token — never from request bodies. <see cref="Reference"/> is the stable key used to
/// find-or-create the matching billing-system customer, which is what makes enrollment
/// idempotent across retries and app restarts.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string reference, string email, string? firstName = null, string? lastName = null)
    {
        Reference = Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName;
        LastName = string.IsNullOrWhiteSpace(lastName) ? null : lastName;
    }

    /// <summary>Stable external key for this user (the eShop username / email).</summary>
    public string Reference { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
