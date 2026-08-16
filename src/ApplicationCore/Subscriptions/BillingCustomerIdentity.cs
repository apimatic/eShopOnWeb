namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The stable identity used to map an eShopOnWeb user onto a customer record in the billing
/// system. <see cref="Reference"/> is the idempotency anchor: it is derived deterministically
/// from the user so the same user always resolves to the same billing customer, even across
/// application restarts (the in-memory DB does not persist the user's numeric id, but the
/// user's email/username is seeded deterministically).
/// </summary>
public sealed class BillingCustomerIdentity
{
    public BillingCustomerIdentity(string reference, string email, string firstName, string lastName)
    {
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Deterministic, unique cross-system reference (stored on the billing customer's <c>reference</c> field).</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }
}
