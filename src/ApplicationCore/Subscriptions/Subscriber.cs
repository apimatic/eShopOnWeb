using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user on whose behalf a billing operation is performed.
/// </summary>
/// <param name="UserKey">
/// Stable, application-side identifier for the user. It is the anchor for the billing
/// provider's customer record, so it must not change over the lifetime of the account.
/// </param>
/// <param name="Email">The user's email address, forwarded to the billing provider for invoicing.</param>
/// <param name="FirstName">Optional given name used when the billing customer is first created.</param>
/// <param name="LastName">Optional family name used when the billing customer is first created.</param>
public record Subscriber(string UserKey, string Email, string? FirstName = null, string? LastName = null)
{
    public static Subscriber ForUser(string userKey, string email, string? firstName = null, string? lastName = null)
    {
        Guard.Against.NullOrWhiteSpace(userKey, nameof(userKey));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        return new Subscriber(userKey, email, firstName, lastName);
    }
}
