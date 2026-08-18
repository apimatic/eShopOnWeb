using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequestBody
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key does not send a second
    /// message; a genuine second attempt uses a fresh key.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    /// <summary>The identifier of the message the resend produced (new, or the one an identical key already produced).</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class ReconciliationEntryDto
{
    public string? Sid { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }

    /// <summary>InSync, ProviderOnly (provider knows, eShop doesn't) or EShopOnly (eShop believes it sent, provider doesn't).</summary>
    public string State { get; set; } = string.Empty;
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>The configured sending number the provider was queried for (the app's own number).</summary>
    public string SendingNumber { get; set; } = string.Empty;

    public int ProviderMessageCount { get; set; }
    public int EShopMessageCount { get; set; }
    public int InSyncCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}
