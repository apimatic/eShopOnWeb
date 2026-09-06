using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper being enrolled, as resolved from the caller's bearer token.
/// </summary>
/// <param name="UserName">
/// The shopper's login name. This is the value the Maxio customer <c>reference</c> is derived from,
/// because it is the identity that is stable across application restarts - unlike the Identity
/// primary key, which is regenerated whenever the in-memory database is reseeded.
/// </param>
/// <param name="Email">Email address recorded on the Maxio customer.</param>
/// <param name="FirstName">Optional given name; falls back to a value derived from the email.</param>
/// <param name="LastName">Optional family name; falls back to a value derived from the email.</param>
public record Subscriber(string UserName, string Email, string? FirstName = null, string? LastName = null)
{
    public string UserName { get; } = Guard.Against.NullOrWhiteSpace(UserName, nameof(UserName));

    public string Email { get; } = Guard.Against.NullOrWhiteSpace(Email, nameof(Email));
}
