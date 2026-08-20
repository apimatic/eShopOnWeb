using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class MaxioCustomerLink
{
    public string UserId { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public DateTimeOffset LastSyncedAt { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public ICollection<MaxioSubscriptionLink> Subscriptions { get; set; } = new List<MaxioSubscriptionLink>();
}
