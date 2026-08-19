using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>
/// A notification about an order as returned by the API. Carries its own <see cref="NotificationId"/>
/// — what the operator endpoints act on — and the provider-owned delivery state. The destination
/// number is deliberately not included.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentDisposed { get; set; }
    public bool ContentAvailable { get; set; }
    public string? ProviderMessageSid { get; set; }
    public int? ResendOfNotificationId { get; set; }
    public DateTimeOffset CreatedDate { get; set; }

    public static NotificationDto FromView(NotificationView v) => new()
    {
        NotificationId = v.NotificationId,
        OrderId = v.OrderId,
        Kind = v.Kind,
        Status = v.Status,
        ErrorCode = v.ErrorCode,
        ErrorMessage = v.ErrorMessage,
        IsScheduled = v.IsScheduled,
        ScheduledFor = v.ScheduledFor,
        ContentDisposed = v.ContentDisposed,
        ContentAvailable = v.ContentAvailable,
        ProviderMessageSid = v.ProviderMessageSid,
        ResendOfNotificationId = v.ResendOfNotificationId,
        CreatedDate = v.CreatedDate
    };

    public static List<NotificationDto> FromViews(IEnumerable<NotificationView> views) =>
        views.Select(FromView).ToList();
}

/// <summary>Response for the notifications of a single order.</summary>
public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>An order with where each of its notifications got to.</summary>
public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>Response listing the caller's orders and their notification outcomes.</summary>
public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}
