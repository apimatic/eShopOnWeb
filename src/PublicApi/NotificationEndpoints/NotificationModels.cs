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
    public string ProviderStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderMessageCount { get; set; }
    public int EShopMessageCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationMessageDto> Matched { get; set; } = new();
    public List<ReconciliationMessageDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEShopOnlyDto> EShopOnly { get; set; } = new();
}

public class ReconciliationMessageDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}

public class ReconciliationEShopOnlyDto
{
    public int NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
}
