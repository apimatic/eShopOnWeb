using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class MaxioSubscriptionLink
{
    public long MaxioSubscriptionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset LastSyncedAt { get; set; }
    public MaxioCustomerLink CustomerLink { get; set; } = null!;
}
