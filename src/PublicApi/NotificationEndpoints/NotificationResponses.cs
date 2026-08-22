using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ResendNotificationResponse()
    {
    }

    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string? ProviderSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? ProviderSid { get; set; }
    public int? NotificationId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EshopStatus { get; set; }
    public string Match { get; set; } = string.Empty;
}
