using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Links an eShopOnWeb user to their customer record in the billing provider.
/// </summary>
public class BillingCustomerClaim : BillingClaim, IAggregateRoot
{
    public int? ProviderCustomerId { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomerClaim() { }

    public BillingCustomerClaim(string userKey, string customerReference, DateTimeOffset now, TimeSpan lease)
    {
        Start(userKey, customerReference, now, lease);
    }

    public void Complete(int providerCustomerId, DateTimeOffset now)
    {
        ProviderCustomerId = providerCustomerId;
        MarkCompleted(now);
    }
}
