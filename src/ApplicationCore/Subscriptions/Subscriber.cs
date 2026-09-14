using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity being billed. <see cref="UserKey"/> is the stable key that is projected onto
/// the billing provider's customer record; that projection is what makes "ensure a customer exists"
/// idempotent without eShopOnWeb persisting a mapping of its own.
/// </summary>
public class Subscriber
{
    public Subscriber(string userKey, string email, string? firstName = null, string? lastName = null)
    {
        if (string.IsNullOrWhiteSpace(userKey))
        {
            throw new ArgumentException("A subscriber requires a non-empty user key.", nameof(userKey));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("A subscriber requires a non-empty email address.", nameof(email));
        }

        UserKey = userKey;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable per-user key, unique within the eShopOnWeb instance (the ASP.NET Identity user name).</summary>
    public string UserKey { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
