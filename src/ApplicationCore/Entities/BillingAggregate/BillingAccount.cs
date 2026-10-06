using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Maps an eShopOnWeb identity user to the Maxio Advanced Billing customer
/// created for that user. One row per user; the mapping is idempotent.
/// </summary>
public class BillingAccount : BaseEntity, IAggregateRoot
{
    public string UserId { get; private set; }
    public int MaxioCustomerId { get; private set; }
    public string CustomerReference { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public BillingAccount(string userId, int maxioCustomerId, string customerReference)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        CustomerReference = customerReference;
        CreatedAtUtc = DateTime.UtcNow;
    }
}