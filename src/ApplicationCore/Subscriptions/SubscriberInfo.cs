namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The authenticated shopper enrolling in / viewing subscriptions. The identity originates from the
/// caller's JWT (resolved to the eShop Identity user), never from the request body.
/// <see cref="UserId"/> is the durable per-user key used as the Maxio customer reference, so the same
/// shopper always maps to the same Maxio customer.
/// </summary>
public class SubscriberInfo
{
    public SubscriberInfo(string userId, string userName, string? email)
    {
        UserId = userId;
        UserName = userName;
        Email = email;
    }

    /// <summary>Stable eShop Identity user id — the Maxio customer reference.</summary>
    public string UserId { get; }

    /// <summary>The eShop username (login).</summary>
    public string UserName { get; }

    /// <summary>The shopper's email, if available.</summary>
    public string? Email { get; }
}
