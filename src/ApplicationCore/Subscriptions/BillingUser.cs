namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper, as the billing layer sees them. <see cref="Reference"/> is the
/// stable external key mirrored onto the Maxio customer so that "ensure customer exists"
/// stays idempotent — a double-click never creates a second customer.
/// </summary>
public class BillingUser
{
    public BillingUser(string reference, string email, string firstName, string lastName)
    {
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable external identifier (the shopper's login/email) used as the Maxio customer reference.</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }
}
