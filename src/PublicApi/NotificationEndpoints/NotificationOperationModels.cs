using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key does not send a second
    /// message; a fresh key is a genuine new attempt. May also be supplied via the Idempotency-Key header.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Set from the route by the endpoint; the message to re-send.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int NotificationId { get; set; }
}

/// <summary>Carries the message whose content is to be disposed of.</summary>
public class DisposeNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; set; }
}

/// <summary>Carries the reconciliation date range.</summary>
public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the resend produced (top-level).</summary>
    public int NotificationId { get; set; }

    /// <summary>True when this request replayed an earlier resend under the same idempotency key (nothing was sent).</summary>
    public bool Replayed { get; set; }

    public string Status { get; set; } = string.Empty;
}

public class ReconciliationEntryDto
{
    public string Sid { get; set; } = string.Empty;
    public bool InProvider { get; set; }
    public bool InEShop { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>The sending number the provider was queried for (Twilio:FromNumber).</summary>
    public string FromNumber { get; set; } = string.Empty;

    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }

    /// <summary>Messages both the provider and eShop know about.</summary>
    public List<ReconciliationEntryDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about but eShop does not.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent but the provider's records for the range do not show.</summary>
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}
