using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper a billing operation is performed on behalf of.
/// </summary>
/// <param name="UserName">
/// Stable login for the shopper. The billing customer record is keyed on this, so it must not
/// change between runs. (The ASP.NET Identity primary key is deliberately not used: under the
/// in-memory database provider it is regenerated on every restart.)
/// </param>
/// <param name="Email">Contact email. Falls back to <paramref name="UserName"/> when absent.</param>
/// <param name="FirstName">Optional given name supplied by the caller.</param>
/// <param name="LastName">Optional family name supplied by the caller.</param>
/// <param name="UserId">Identity primary key, carried for logging and correlation only.</param>
public sealed record SubscriberIdentity(
    string UserName,
    string? Email = null,
    string? FirstName = null,
    string? LastName = null,
    string? UserId = null)
{
    public string EmailAddress => string.IsNullOrWhiteSpace(Email) ? UserName : Email!;

    public SubscriberIdentity Validated()
    {
        Guard.Against.NullOrWhiteSpace(UserName, nameof(UserName));
        return this;
    }
}
