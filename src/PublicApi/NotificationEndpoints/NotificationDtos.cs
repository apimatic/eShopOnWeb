using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>A notification as returned by the API. The destination number is masked to its last four digits.</summary>
public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ToNumberMasked { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static OrderNotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        ToNumberMasked = PhoneNumberMasker.Mask(n.ToNumber),
        ProviderMessageSid = n.ProviderMessageSid,
        Status = n.Status,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ContentRedacted = n.ContentRedacted,
        CreatedAt = n.CreatedAt
    };
}

/// <summary>One order in the shopper's my-orders view, with its notifications.</summary>
public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
