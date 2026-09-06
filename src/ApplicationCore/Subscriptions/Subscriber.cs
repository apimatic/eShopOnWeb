using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper a billing customer is created for. <see cref="UserName"/> is the identity
/// carried by the caller's token and is what durably links the shopper to the billing system.
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
        Email = string.IsNullOrWhiteSpace(email) ? userName : email;
        FirstName = firstName;
        LastName = lastName;
    }

    public string UserName { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
