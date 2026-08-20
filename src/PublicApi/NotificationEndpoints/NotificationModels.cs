using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    public int NotificationId { get; set; }
    public NotificationDto Notification { get; set; } = new();
}

public class DisposeNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; init; }

    public DisposeNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}

public class ReconciliationQueryRequest : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    public ReconciliationQueryRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int ApplicationOnlyCount { get; set; }
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ProviderMessageDto> ProviderOnly { get; set; } = new();
    public List<NotificationDto> ApplicationOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public NotificationDto Application { get; set; } = new();
    public ProviderMessageDto Provider { get; set; } = new();
}

public class ProviderMessageDto
{
    public string Sid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Body { get; set; }
    public string? DateSent { get; set; }
    public string? DateCreated { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
