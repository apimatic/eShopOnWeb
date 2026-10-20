using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Links a shopper to their customer record at the billing provider.
/// </summary>
public class BillingCustomerClaim : BillingClaim
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingCustomerClaim() { }

    public BillingCustomerClaim(string reference, string buyerId, DateTimeOffset now)
        : base(reference, buyerId, now)
    {
    }

    /// <summary>The provider's id for the customer, once known.</summary>
    public int? BillingCustomerId { get; private set; }

    public void Complete(int billingCustomerId, DateTimeOffset now)
    {
        BillingCustomerId = billingCustomerId;
        MarkCompleted(now);
    }
}
