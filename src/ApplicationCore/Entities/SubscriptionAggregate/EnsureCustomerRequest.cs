using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Identifies the eShopOnWeb user whose provider-side customer record must exist (UC1 step 3).
/// Resolution is idempotent on <see cref="Reference"/>.
/// </summary>
public sealed class EnsureCustomerRequest
{
    public EnsureCustomerRequest(string reference, string email, string? firstName = null, string? lastName = null)
    {
        Reference = Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>The stable eShopOnWeb user reference (username/email) — see §4.4.</summary>
    public string Reference { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
