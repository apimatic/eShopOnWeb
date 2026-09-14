using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper that a billing operation is performed on behalf of.
/// <para>
/// <see cref="UserName"/> is the stable key: the billing system of record is keyed on it, so the
/// mapping between an eShopOnWeb user and their billing customer survives application restarts
/// without any local persistence.
/// </para>
/// </summary>
public class BillingIdentity
{
    public BillingIdentity(string userName, string? email = null, string? firstName = null, string? lastName = null)
    {
        UserName = Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Email = string.IsNullOrWhiteSpace(email) ? userName : email!;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>The eShopOnWeb user name (unique per user; in eShopOnWeb this is the e-mail address).</summary>
    public string UserName { get; }

    /// <summary>Contact e-mail for the billing customer record.</summary>
    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
