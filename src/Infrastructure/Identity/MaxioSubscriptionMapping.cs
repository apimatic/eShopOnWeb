using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable link between an eShopOnWeb identity and the corresponding Maxio records.
/// </summary>
public class MaxioSubscriptionMapping
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int MaxioCustomerId { get; set; }

    public int MaxioSubscriptionId { get; set; }

    public string ProductHandle { get; set; } = string.Empty;

    public string SubscriptionReference { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
