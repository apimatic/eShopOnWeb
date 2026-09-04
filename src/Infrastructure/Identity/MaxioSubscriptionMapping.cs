using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable link between an eShop user and the corresponding Maxio subscription.
/// </summary>
public class MaxioSubscriptionMapping
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public long MaxioCustomerId { get; set; }

    public long MaxioSubscriptionId { get; set; }

    public string ProductHandle { get; set; } = string.Empty;

    public string SubscriptionReference { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
