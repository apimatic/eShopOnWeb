using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper a billing operation is performed on behalf of. This is the only identity the
/// billing layer ever sees; it is projected from the authenticated principal by the API layer and is
/// never taken from the request body.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string userName, string? email = null, string? firstName = null,
        string? lastName = null, string? organization = null)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A subscriber must have a user name.", nameof(userName));
        }

        UserName = userName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? UserName : email!.Trim();
        FirstName = Normalize(firstName);
        LastName = Normalize(lastName);
        Organization = Normalize(organization);
    }

    /// <summary>The eShopOnWeb user name (the value carried in the JWT name claim).</summary>
    public string UserName { get; }

    /// <summary>The e-mail address registered with the billing provider.</summary>
    public string Email { get; }

    /// <summary>Optional given name. When absent the billing layer derives one from <see cref="Email"/>.</summary>
    public string? FirstName { get; }

    /// <summary>Optional family name. When absent the billing layer derives one from <see cref="Email"/>.</summary>
    public string? LastName { get; }

    /// <summary>Optional organization/company name.</summary>
    public string? Organization { get; }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
