namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identity of the eShop user on whose behalf a billing operation is performed.
/// <para>
/// <see cref="Reference"/> is the stable, unique key that maps an eShop user to a single
/// customer in the billing system. Using a stable reference (the user's id) is what makes
/// customer creation idempotent: repeated subscribe attempts always resolve to the same
/// billing customer rather than creating duplicates.
/// </para>
/// </summary>
public class BillingCustomerInfo
{
    public BillingCustomerInfo(string reference, string email, string firstName, string lastName)
    {
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable, unique identifier for the user in eShop (used as the billing customer reference).</summary>
    public string Reference { get; }

    /// <summary>The user's email address.</summary>
    public string Email { get; }

    /// <summary>First name supplied to the billing system (required by most providers).</summary>
    public string FirstName { get; }

    /// <summary>Last name supplied to the billing system (required by most providers).</summary>
    public string LastName { get; }
}
