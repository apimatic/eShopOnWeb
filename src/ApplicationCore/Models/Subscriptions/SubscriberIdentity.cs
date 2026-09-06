using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// The eShopOnWeb user on whose behalf a billing operation is performed. Always derived from the
/// authenticated principal on the server; never accepted from a request body.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string userName, string email, string? firstName = null, string? lastName = null)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("A subscriber must have a user name.", nameof(userName));

        UserName = userName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? UserName : email.Trim();
        FirstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName!.Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? null : lastName!.Trim();
    }

    public string UserName { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
