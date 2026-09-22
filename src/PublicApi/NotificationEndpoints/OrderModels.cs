using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class PlaceOrderRequest
{
    [Required]
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used when omitted (notifications are the feature under test).</summary>
    public AddressDto? ShipToAddress { get; set; }
}

public class PlaceOrderResponse
{
    /// <summary>The identifier of the order that was placed (top-level, so callers can drive the flow).</summary>
    public int OrderId { get; set; }
}

/// <summary>One notification and its current provider outcome.</summary>
public class NotificationDto
{
    /// <summary>The identifier the operator endpoints act on.</summary>
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string? ProviderSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string NotificationStatus { get; set; } = string.Empty;
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class MyOrdersResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

public class OrderNotificationsResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
