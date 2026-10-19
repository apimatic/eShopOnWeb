using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Maps an eShop user (by billing reference) to the billing provider's customer.
/// </summary>
public class BillingCustomer : BillingClaim, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomer() { }

    public BillingCustomer(string customerReference, DateTimeOffset now) : base(customerReference, now)
    {
    }

    public int? ProviderCustomerId { get; private set; }

    public void Activate(int providerCustomerId, DateTimeOffset now)
    {
        ProviderCustomerId = providerCustomerId;
        State = BillingClaimState.Active;
        Touch(now);
    }
}
