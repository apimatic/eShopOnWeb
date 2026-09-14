using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity of the shopper being enrolled in a subscription.
/// This is the local (Identity) view of the user; the Maxio customer that mirrors
/// it is resolved/created by <see cref="Interfaces.IMaxioSubscriptionService"/> using
/// a deterministic external reference derived from <see cref="UserId"/>.
/// </summary>
public sealed class SubscriberIdentity
{
    public SubscriberIdentity(string userId, string email, string? firstName = null, string? lastName = null)
    {
        UserId = Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = string.IsNullOrWhiteSpace(firstName) ? DeriveFirstName(email) : firstName!;
        LastName = string.IsNullOrWhiteSpace(lastName) ? "eShopOnWeb" : lastName!;
    }

    /// <summary>The stable ASP.NET Identity user id. Used to derive the Maxio customer reference.</summary>
    public string UserId { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    private static string DeriveFirstName(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        return string.IsNullOrWhiteSpace(local) ? "eShop" : local;
    }
}
