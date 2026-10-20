using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Links an eShopOnWeb user to their customer record at the billing provider.
/// <see cref="BillingClaim.Key"/> is the customer reference derived from the user name.
/// </summary>
public class BillingCustomerLink : BillingClaim
{
    private BillingCustomerLink()
    {
        // required by EF
    }

    public BillingCustomerLink(string customerReference, DateTimeOffset now) : base(customerReference, now)
    {
    }

    public int? ProviderCustomerId { get; private set; }

    public void MarkLinked(int providerCustomerId, DateTimeOffset now)
    {
        ProviderCustomerId = providerCustomerId;
        MarkCompletedCore(now);
    }
}
