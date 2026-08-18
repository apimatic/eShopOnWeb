using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();

    /// <summary>Optional shipping address. A placeholder is used when omitted.</summary>
    public AddressRequest? ShipToAddress { get; set; }
}

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class OrderTransitionResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>The state a single notification reached — carries its own notificationId for operator actions.</summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's current delivery outcome, or a local "not_sent"/"scheduled" state.</summary>
    public string DeliveryStatus { get; set; } = string.Empty;

    public string? ProviderSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public bool SendFailed { get; set; }
    public string? SendFailureReason { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
}

public class OrderWithNotificationsDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderWithNotificationsDto> Orders { get; set; } = new();
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
