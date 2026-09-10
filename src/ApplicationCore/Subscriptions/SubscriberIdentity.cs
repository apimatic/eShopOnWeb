namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb-side identity of a shopper being enrolled in recurring billing.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Reference"/> is the stable key used to correlate an eShopOnWeb user with a single
/// Maxio customer record (persisted as the Maxio customer's <c>reference</c> attribute). Because
/// Maxio is the billing system of record, keying on a value that survives an eShopOnWeb restart is
/// what makes "ensure a customer exists" idempotent even though this machine runs against an
/// in-memory identity store that regenerates surrogate user ids on every launch.
/// </para>
/// </remarks>
public sealed record SubscriberIdentity
{
    public SubscriberIdentity(string reference, string email, string firstName, string lastName)
    {
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable, unique identifier for the shopper (used as the Maxio customer reference).</summary>
    public string Reference { get; }

    /// <summary>The shopper's email address.</summary>
    public string Email { get; }

    /// <summary>Given name recorded on the Maxio customer (required by the customer contract).</summary>
    public string FirstName { get; }

    /// <summary>Family name recorded on the Maxio customer (required by the customer contract).</summary>
    public string LastName { get; }
}
