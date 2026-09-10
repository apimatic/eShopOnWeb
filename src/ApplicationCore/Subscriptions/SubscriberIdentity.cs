using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb user that a billing operation is performed on behalf of.
/// The <see cref="Reference"/> is the stable key used to correlate an eShopOnWeb user
/// with a single Maxio customer record (Maxio enforces uniqueness on customer reference),
/// which is what makes "ensure a customer exists" idempotent.
/// </summary>
public sealed class SubscriberIdentity
{
    public SubscriberIdentity(string userName, string email, string? firstName = null, string? lastName = null)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        UserName = userName;
        Email = email;
        FirstName = string.IsNullOrWhiteSpace(firstName) ? DeriveFirstName(email) : firstName!;
        LastName = string.IsNullOrWhiteSpace(lastName) ? "eShopOnWeb" : lastName!;
    }

    /// <summary>The eShopOnWeb identity (user name, taken from the JWT).</summary>
    public string UserName { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    private static string DeriveFirstName(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email.Substring(0, at) : email;
        return string.IsNullOrWhiteSpace(local) ? "eShop" : local;
    }
}
