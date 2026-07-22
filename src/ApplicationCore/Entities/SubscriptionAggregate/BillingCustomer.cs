using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user.
/// </summary>
/// <remarks>
/// <see cref="Reference"/> carries the eShopOnWeb user's stable identity reference
/// (their username / email). It is what makes "ensure a customer exists" idempotent:
/// the same eShopOnWeb user always resolves to the same provider-side customer.
/// </remarks>
public class BillingCustomer
{
    public BillingCustomer(int id, string reference, string email)
    {
        Guard.Against.NullOrEmpty(reference, nameof(reference));
        Guard.Against.NullOrEmpty(email, nameof(email));

        Id = id;
        Reference = reference;
        Email = email;
    }

    public int Id { get; }

    /// <summary>The eShopOnWeb user reference this customer was created for.</summary>
    public string Reference { get; }

    public string Email { get; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }
}
