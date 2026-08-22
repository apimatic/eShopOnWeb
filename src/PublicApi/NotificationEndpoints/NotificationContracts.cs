using System;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public int OriginalNotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? DeliveryStatus { get; set; }
}

public class ReconciliationMessageDto
{
    public int? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? DeliveryStatus { get; set; }
    public int? OrderId { get; set; }
    public string? Kind { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public ReconciliationMessageDto[] Matched { get; set; } = [];
    public ReconciliationMessageDto[] ProviderOnly { get; set; } = [];
    public ReconciliationMessageDto[] ApplicationOnly { get; set; } = [];
}
