using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user. <see cref="Reference"/> carries the
/// eShopOnWeb identity (the signed-in user's email/username) and is what makes repeated
/// "ensure customer" calls idempotent.
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(int id, string? reference, string email)
    {
        Guard.Against.NullOrEmpty(email, nameof(email));

        Id = id;
        Reference = reference;
        Email = email;
    }

    public int Id { get; }

    /// <summary>The eShopOnWeb user reference this provider customer is keyed on.</summary>
    public string? Reference { get; }

    public string Email { get; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }
}
