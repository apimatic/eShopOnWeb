using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>Places an order from catalog items. Identity comes from the token; only the lines are supplied.</summary>
public class PlaceOrderRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();

    /// <summary>Optional shipping address. A placeholder is used when omitted (the notification flow does not depend on it).</summary>
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

/// <summary>Response to placing an order; carries the new order id at the top level.</summary>
public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

/// <summary>Where a notification got to, as shown alongside an order.</summary>
public class NotificationStatusDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool Delivered { get; set; }
    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledForUtc { get; set; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<NotificationStatusDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>A notification's full detail. <c>notificationId</c> is what operator endpoints act on.</summary>
public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool Delivered { get; set; }
    public bool IsScheduled { get; set; }
    public DateTimeOffset? ScheduledForUtc { get; set; }
    public string? ProviderMessageSid { get; set; }

    /// <summary>The message text. Null once its content has been disposed of.</summary>
    public string? MessageBody { get; set; }

    public bool ContentRedacted { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
