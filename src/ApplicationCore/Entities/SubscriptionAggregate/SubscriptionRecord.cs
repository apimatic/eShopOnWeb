using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Local record linking an eShopOnWeb user to a Maxio Advanced Billing subscription.
/// It is the persistence side of subscribe idempotency and is reconciled with Maxio on read.
/// </summary>
public class SubscriptionRecord : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int MaxioSubscriptionId { get; set; }

    /// <summary>
    /// The value sent as Maxio subscription `reference` for this enrollment intent.
    /// </summary>
    public string MaxioReference { get; set; } = string.Empty;

    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
