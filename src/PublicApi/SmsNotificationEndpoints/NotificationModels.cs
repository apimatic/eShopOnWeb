using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>Re-sends a message. The idempotency key may also be supplied via the <c>Idempotency-Key</c> header.</summary>
public class ResendNotificationRequest
{
    public string? IdempotencyKey { get; set; }
}

/// <summary>Response to a resend; carries the identifier of the message the resend produced.</summary>
public class ResendNotificationResponse
{
    public int NotificationId { get; set; }

    /// <summary>True when a prior request under the same idempotency key already produced this message (nothing new was sent).</summary>
    public bool Reused { get; set; }
}

public class ReconciliationEntryDto
{
    /// <summary>InBoth, ProviderOnly, or EShopOnly.</summary>
    public string Match { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? EShopStatus { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }

    /// <summary>The configured sending number the provider was asked about.</summary>
    public string FromNumber { get; set; } = string.Empty;

    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int InBothCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}
