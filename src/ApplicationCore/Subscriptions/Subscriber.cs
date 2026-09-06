using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity being billed. <see cref="UserName"/> is the natural key: the billing
/// customer record is keyed on it, so the link between an eShopOnWeb user and their billing
/// customer survives application restarts without any local persistence.
/// </summary>
public class Subscriber
{
    public Subscriber(string userName, string? email = null, string? firstName = null, string? lastName = null)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A subscriber must have a user name.", nameof(userName));
        }

        UserName = userName;
        Email = string.IsNullOrWhiteSpace(email) ? userName : email!;
        FirstName = firstName;
        LastName = lastName;
    }

    public string UserName { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
