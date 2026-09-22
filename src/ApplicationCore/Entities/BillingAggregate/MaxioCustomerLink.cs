using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Local claim + mapping between an eShop user and their Maxio customer record. The unique index on
/// <see cref="BuyerId"/> (configured in Infrastructure) is the duplicate-prevention claim: a second
/// concurrent attempt to create a customer for the same user is rejected by the constraint, which the
/// billing service catches. Maxio remains the system of record; this row is a claim/cache, rebuildable
/// from Maxio via the customer's <c>reference</c>.
/// </summary>
public class MaxioCustomerLink : BaseEntity
{
    public string BuyerId { get; private set; } = default!;
    public int MaxioCustomerId { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }

    private MaxioCustomerLink() { } // EF

    public MaxioCustomerLink(string buyerId, int maxioCustomerId, DateTimeOffset createdDate)
    {
        BuyerId = buyerId;
        MaxioCustomerId = maxioCustomerId;
        CreatedDate = createdDate;
    }
}
