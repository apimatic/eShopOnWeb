using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string? ProductName { get; set; }
    public int Units { get; set; }
    public decimal UnitPrice { get; set; }
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;

    public Address ToAddress() => new(Street, City, State, Country, ZipCode);
}

public class OrderNotificationDto
{
    public int NotificationId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotificationKind Kind { get; set; }

    public string? Status { get; set; }
    public string? ProviderMessageSid { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public int? ResendOfNotificationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static OrderNotificationDto FromEntity(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Kind = notification.Kind,
        Status = notification.Status,
        ProviderMessageSid = notification.ProviderMessageSid,
        ProviderErrorCode = notification.ProviderErrorCode,
        ProviderErrorMessage = notification.ProviderErrorMessage,
        Body = notification.Body,
        ContentRedacted = notification.ContentRedacted,
        ScheduledFor = notification.ScheduledFor,
        ResendOfNotificationId = notification.ResendOfNotificationId,
        CreatedAt = notification.CreatedAt
    };
}

public class OrderDto
{
    public int OrderId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OrderStatus Status { get; set; }

    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
