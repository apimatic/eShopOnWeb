using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// The eShopOnWeb identity a billing customer is created for. This is the only thing the billing
/// provider is told about the shopper, and <see cref="UserName"/> is the value the provider-side
/// customer record is keyed on, so it has to be stable for the lifetime of the account.
/// </summary>
public sealed class BillingCustomerIdentity
{
    public BillingCustomerIdentity(string userName, string? email)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A billing customer identity requires a user name.", nameof(userName));
        }

        UserName = userName;
        Email = string.IsNullOrWhiteSpace(email) ? null : email;
    }

    /// <summary>The eShopOnWeb user name. Stable across restarts; used to derive the provider reference.</summary>
    public string UserName { get; }

    /// <summary>The user's email address, when the account has one.</summary>
    public string? Email { get; }
}
