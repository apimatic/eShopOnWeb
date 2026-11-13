using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Links an eShopOnWeb user to their customer record at the billing provider.
/// <see cref="BillingClaim.Reference"/> is the customer reference derived from the user name.
/// </summary>
public class BillingCustomerLink : BillingClaim
{
    public BillingCustomerLink(string reference, string userName, DateTime nowUtc, TimeSpan lease)
        : base(reference, nowUtc, lease)
    {
        UserName = Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
    }

#pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomerLink() { }
#pragma warning restore CS8618

    public string UserName { get; private set; }
}
