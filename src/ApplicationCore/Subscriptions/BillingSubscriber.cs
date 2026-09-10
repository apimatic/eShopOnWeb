using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The identity of an eShopOnWeb user as seen by the billing system of record (Maxio).
/// <see cref="UserId"/> is used as the stable, unique customer <c>reference</c> in Maxio so that
/// looking a customer up (or creating one) is idempotent for a given eShopOnWeb user.
/// </summary>
public class BillingSubscriber
{
    public BillingSubscriber(string userId, string email, string? firstName = null, string? lastName = null)
    {
        UserId = Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));

        // Maxio requires first and last name on a customer. eShopOnWeb identity only carries an
        // email, so derive sensible, deterministic values when nothing better is available.
        FirstName = string.IsNullOrWhiteSpace(firstName) ? DeriveFirstName(email) : firstName!;
        LastName = string.IsNullOrWhiteSpace(lastName) ? "eShopOnWeb" : lastName!;
    }

    /// <summary>Stable eShopOnWeb user id. Used as the Maxio customer <c>reference</c>.</summary>
    public string UserId { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    private static string DeriveFirstName(string email)
    {
        var atIndex = email.IndexOf('@');
        var localPart = atIndex > 0 ? email.Substring(0, atIndex) : email;
        return string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb Customer" : localPart;
    }
}
