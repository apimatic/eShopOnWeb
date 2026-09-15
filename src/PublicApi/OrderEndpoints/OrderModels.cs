using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class OrderAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();

    /// <summary>Optional ship-to address; a placeholder is used when omitted.</summary>
    public OrderAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
}

/// <summary>
/// The view of a notification returned to callers. Deliberately excludes the destination number (personal
/// data). The body is null once its content has been disposed of.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string SendState { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public bool ContentRedacted { get; set; }
    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Body { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        SendState = n.SendState.ToString(),
        ProviderStatus = n.ProviderStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        ContentRedacted = n.ContentRedacted,
        IsScheduled = n.IsScheduled,
        ScheduledSendAt = n.ScheduledSendAt,
        CreatedAt = n.CreatedAt,
        Body = n.Body
    };
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<MyOrderItemDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}
