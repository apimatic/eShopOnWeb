using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public OrderStatus Status { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class NotificationDto
{
    public int NotificationId { get; set; }
    public OrderNotificationKind Kind { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentRedacted { get; set; }
}
