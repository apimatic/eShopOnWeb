using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user.
/// <para>
/// <see cref="Reference"/> is the stable eShopOnWeb identity (the signed-in user's email/username)
/// and is what makes customer creation idempotent: the same user always resolves to the same
/// provider-side customer, so a repeated subscribe never creates a duplicate.
/// </para>
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(int id, string reference, string email, string? firstName = null, string? lastName = null)
    {
        Guard.Against.NullOrEmpty(reference, nameof(reference));

        Id = id;
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public int Id { get; }

    /// <summary>The eShopOnWeb user reference this customer was created for.</summary>
    public string Reference { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
