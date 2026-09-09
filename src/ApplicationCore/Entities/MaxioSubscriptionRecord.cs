using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Local mapping between an eShopOnWeb user and their Maxio (Advanced Billing)
/// subscription. Maxio remains the system of record for subscription state;
/// this record only persists the identity linkage so we can look a user's
/// subscriptions back up without needing customer_id filters on the Maxio API.
/// </summary>
public class MaxioSubscriptionRecord : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string MaxioSubscriptionReference { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
