using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user on whose behalf a billing operation is performed. The caller's identity
/// always comes from the authenticated principal (the JWT), never from request input, so a
/// shopper can only ever act on their own billing account.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string userId, string email)
    {
        UserId = Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
    }

    /// <summary>The ASP.NET Identity user id (stable within a run).</summary>
    public string UserId { get; }

    /// <summary>The user's email; in eShopOnWeb this doubles as the username / login.</summary>
    public string Email { get; }
}
