namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user that a billing operation is performed on behalf of. Built from the
/// authenticated caller's identity (never from request input) so it can be used as a stable,
/// idempotent key when mapping the user onto a billing-provider customer.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string reference, string email, string firstName, string lastName)
    {
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>
    /// Deterministic, per-user key. The same value is used both to create the provider customer
    /// and to look one up, which is what makes enrollment idempotent across double-clicks.
    /// </summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }
}
