using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The billing provider's record of an eShopOnWeb user. <see cref="Reference"/> carries the
/// eShopOnWeb user identity and is what makes customer creation idempotent.
/// </summary>
public class BillingCustomer
{
    public BillingCustomer(int id, string? reference, string email)
    {
        Guard.Against.NegativeOrZero(id, nameof(id));

        Id = id;
        Reference = reference;
        Email = email;
    }

    public int Id { get; private set; }

    /// <summary>The eShopOnWeb user reference (username/email) this customer was created for.</summary>
    public string? Reference { get; private set; }

    public string Email { get; private set; }
}
