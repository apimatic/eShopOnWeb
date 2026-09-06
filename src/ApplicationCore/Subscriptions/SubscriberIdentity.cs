using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb-side identity of the person being enrolled. Everything the billing system needs to
/// create (or re-find) its own customer record is carried here, so the billing implementation never
/// reaches back into ASP.NET Core Identity.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string userName, string? email = null, string? firstName = null, string? lastName = null)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A subscriber must have a user name.", nameof(userName));
        }

        UserName = userName;
        Email = string.IsNullOrWhiteSpace(email) ? userName : email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>
    /// The eShopOnWeb user name. This is the stable business key the billing customer reference is
    /// derived from — deliberately not the Identity primary key, which is regenerated whenever the
    /// app runs against the in-memory database.
    /// </summary>
    public string UserName { get; }

    public string Email { get; }

    /// <summary>Optional; when absent the billing implementation derives a placeholder from the email.</summary>
    public string? FirstName { get; }

    /// <summary>Optional; when absent the billing implementation derives a placeholder from the email.</summary>
    public string? LastName { get; }
}
