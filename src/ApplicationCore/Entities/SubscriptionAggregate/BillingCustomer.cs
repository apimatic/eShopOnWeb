using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user. <see cref="Reference"/> carries the
/// eShopOnWeb identity (the signed-in user's email/username) and is what makes repeated
/// subscribe calls idempotent — see plan.md §4.4.
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(int id, string reference, string email, string? firstName, string? lastName)
    {
        Guard.Against.NullOrEmpty(reference, nameof(reference));

        Id = id;
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public int Id { get; }

    /// <summary>The stable eShopOnWeb user reference (email / username).</summary>
    public string Reference { get; }

    public string Email { get; }
    public string? FirstName { get; }
    public string? LastName { get; }
}
