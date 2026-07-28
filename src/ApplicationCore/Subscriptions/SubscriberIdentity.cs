namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user on whose behalf a billing customer is provisioned. <see cref="Reference"/>
/// is the stable key used to make customer provisioning idempotent — the same reference always
/// resolves to the same billing customer, so a double-click never creates a duplicate.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string reference, string email, string? firstName = null, string? lastName = null)
    {
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable idempotency key (the authenticated user's identity, e.g. their email).</summary>
    public string Reference { get; }

    /// <summary>Contact email for the billing customer.</summary>
    public string Email { get; }

    /// <summary>Optional given name.</summary>
    public string? FirstName { get; }

    /// <summary>Optional family name.</summary>
    public string? LastName { get; }
}
