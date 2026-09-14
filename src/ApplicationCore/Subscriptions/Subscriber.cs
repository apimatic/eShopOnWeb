namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb user being enrolled in a billing plan. The
/// <see cref="Reference"/> is the stable, app-owned identifier used to correlate
/// this user with a customer record in the billing system (Maxio), which makes
/// customer creation idempotent.
/// </summary>
public record Subscriber
{
    public Subscriber(string reference, string email, string firstName, string lastName)
    {
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable identifier for this user within eShopOnWeb (used as the billing customer reference).</summary>
    public string Reference { get; }

    /// <summary>Email address used for the billing customer record.</summary>
    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }
}
