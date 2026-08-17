using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationBody
{
    /// <summary>
    /// Caller-supplied idempotency key. Repeating a request under the same key must not send a second
    /// message; a fresh key is a genuine second attempt. May also be supplied via the
    /// <c>Idempotency-Key</c> request header.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the message the resend produced (or the prior one, on an idempotent replay).</summary>
    public int NotificationId { get; set; }

    /// <summary>True when this call reused an earlier result under the same idempotency key.</summary>
    public bool IdempotentReplay { get; set; }
}

public class DisposeNotificationContentResponse : BaseResponse
{
    public DisposeNotificationContentResponse(Guid correlationId) : base(correlationId) { }
    public DisposeNotificationContentResponse() { }

    public int NotificationId { get; set; }
    public bool ContentDisposed { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int EShopRecordCount { get; set; }
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ProviderMessageDto> ProviderOnly { get; set; } = new();
    public List<EShopNotificationDto> EShopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string EShopStatus { get; set; } = string.Empty;
}

public class ProviderMessageDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public int? ErrorCode { get; set; }
}

public class EShopNotificationDto
{
    public int NotificationId { get; set; }
    public string ProviderMessageSid { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string EShopStatus { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
