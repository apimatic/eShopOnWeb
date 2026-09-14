using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user a subscription belongs to, as resolved from the caller's bearer token.
/// </summary>
public class Subscriber
{
    public Subscriber(string userId, string userName, string email, string? firstName = null, string? lastName = null)
    {
        UserId = Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        UserName = Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>ASP.NET Identity key for the user.</summary>
    public string UserId { get; }

    /// <summary>
    /// The user name carried by the JWT. In eShopOnWeb this is the user's email address, and it is
    /// the identity that stays stable across application restarts, so it is the natural key to tie
    /// an eShopOnWeb user to a billing customer.
    /// </summary>
    public string UserName { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
