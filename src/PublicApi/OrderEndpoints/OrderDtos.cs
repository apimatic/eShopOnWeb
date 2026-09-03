using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

// ------------------------------------------------------------------ requests

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Body of POST /api/orders.</summary>
public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();

    [JsonIgnore]
    public string OwnerId { get; set; } = string.Empty;
}

public class OrderIdRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string OwnerId { get; set; } = string.Empty;
}

public class MyOrdersRequest : BaseRequest
{
    public string OwnerId { get; set; } = string.Empty;
}

// ------------------------------------------------------------------ responses

/// <summary>Response of POST /api/orders — carries the new identifier at the top level.</summary>
public class CreateOrderResponse
{
    public int OrderId { get; set; }
}

/// <summary>Response of dispatch/cancel.</summary>
public class OrderActionResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>The view of one notification, including its own identifier (what operator endpoints act on).</summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    /// <summary>The provider's identifier for the message (its message SID), when one exists.</summary>
    public string? ProviderMessageSid { get; set; }
    /// <summary>The provider's current delivery outcome for the message.</summary>
    public string? DeliveryStatus { get; set; }
    public bool IsFollowUp { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public bool ContentDisposed { get; set; }
    public bool SendFailed { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public int? ResendOfNotificationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Note: the destination number and message body are deliberately not exposed.
    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        ProviderMessageSid = n.ProviderSid,
        DeliveryStatus = n.DeliveryStatus,
        IsFollowUp = n.IsFollowUp,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentDisposed = n.ContentDisposed,
        SendFailed = n.SendFailed,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        ResendOfNotificationId = n.ResendOfNotificationId,
        CreatedAt = n.CreatedAt
    };
}

/// <summary>Response of GET /api/orders/{orderId}/notifications.</summary>
public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>Response of GET /api/my-orders.</summary>
public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}
