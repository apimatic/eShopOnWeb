namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb shopper to the billing system. <see cref="Reference"/> is the
/// stable, unique key used to guarantee a single billing customer per shopper (idempotency).
/// </summary>
public sealed class SubscriberIdentity
{
    public SubscriberIdentity(string reference, string email, string firstName, string lastName)
    {
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable unique identifier for the shopper from eShopOnWeb (used as the billing customer reference).</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }
}
