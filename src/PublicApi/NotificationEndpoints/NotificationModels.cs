using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Caller-supplied idempotency key. A repeat under the same key must not send a second message.</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the resend produced (top-level, so the flow can be driven end to end).</summary>
    public int NotificationId { get; set; }

    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string Outcome { get; set; } = string.Empty;

    /// <summary>True when this key had already been used and no new message was sent.</summary>
    public bool Deduplicated { get; set; }
}

public class ReconciliationEntry
{
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? NotificationId { get; set; }
    public string? EShopStatus { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>The sending number the provider was asked about (Twilio:FromNumber).</summary>
    public string FromNumber { get; set; } = string.Empty;

    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }

    /// <summary>Messages both the provider and eShop agree on.</summary>
    public List<ReconciliationEntry> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop has no record of.</summary>
    public List<ReconciliationEntry> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider has no record of.</summary>
    public List<ReconciliationEntry> EShopOnly { get; set; } = new();
}
