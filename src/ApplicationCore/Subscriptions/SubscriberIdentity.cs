using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb user on whose behalf a subscription operation runs. Identity is taken from the
/// authenticated caller's token (the <c>Name</c> claim), never from the request body, so a caller can
/// only ever act as themselves. <see cref="UserName"/> is the stable key used as the Maxio customer
/// <c>reference</c>, which is what makes "ensure a customer exists" idempotent across calls.
/// </summary>
public sealed class SubscriberIdentity
{
    public SubscriberIdentity(string userName, string? email = null, string? firstName = null, string? lastName = null)
    {
        UserName = Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Email = string.IsNullOrWhiteSpace(email) ? userName : email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>The eShopOnWeb user name (the token's <c>Name</c> claim). Used as the Maxio customer reference.</summary>
    public string UserName { get; }

    /// <summary>The customer's email. In eShopOnWeb the user name is the email, so it defaults to <see cref="UserName"/>.</summary>
    public string Email { get; }

    /// <summary>Optional given name; when absent the billing service derives a non-empty value (Maxio requires one).</summary>
    public string? FirstName { get; }

    /// <summary>Optional family name; when absent the billing service derives a non-empty value (Maxio requires one).</summary>
    public string? LastName { get; }
}
