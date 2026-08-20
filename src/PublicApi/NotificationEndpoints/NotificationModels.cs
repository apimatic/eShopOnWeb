using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    public int NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string DeliveryStatus { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int ApplicationOnlyCount { get; set; }
    public List<ReconciliationItemDto> Matched { get; set; } = new();
    public List<ReconciliationItemDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationItemDto> ApplicationOnly { get; set; } = new();
}

public class ReconciliationItemDto
{
    public int? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ApplicationStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public DateTimeOffset? ApplicationCreatedAt { get; set; }
}
