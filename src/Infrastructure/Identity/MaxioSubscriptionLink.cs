using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class MaxioSubscriptionLink
{
    public string UserId { get; set; } = null!;
    public string ProductHandle { get; set; } = null!;
    public int MaxioSubscriptionId { get; set; }
    public string SubscriptionReference { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; }
}
