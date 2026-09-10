namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user on whose behalf a billing operation runs. The <see cref="Reference"/> is the
/// stable identity (derived from the authenticated caller) used to correlate the eShop user with a
/// single billing-system customer — making customer creation idempotent.
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

    /// <summary>Stable app-side identity of the user (used as the billing customer reference).</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }
}
