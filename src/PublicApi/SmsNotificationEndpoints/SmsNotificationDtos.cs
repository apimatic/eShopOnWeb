using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

// ---- Requests ----

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalize.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateOrderRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used when omitted.</summary>
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class ResendNotificationRequest
{
    /// <summary>Caller-supplied idempotency key; a repeat under the same key does not send a second message.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

// ---- Responses ----

public record ContactNumberDto(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedDate);

public record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);

public record NotificationDto(
    int NotificationId,
    int OrderId,
    string Type,
    string Status,
    string? Body,
    bool ContentDisposed,
    string? ProviderMessageSid,
    bool IsScheduled,
    DateTimeOffset? ScheduledSendAt,
    int? ProviderErrorCode,
    DateTimeOffset CreatedDate);

public record OrderLineDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record OrderDto(int OrderId, string Status, DateTimeOffset OrderDate, decimal Total, IReadOnlyList<OrderLineDto> Items);

public record CreateOrderResponse(int OrderId, string Status, decimal Total);

public record OrderActionResponse(int OrderId, string Status);

public record OrderWithNotificationsDto(OrderDto Order, IReadOnlyList<NotificationDto> Notifications);

public record ResendNotificationResponse(int NotificationId, string Status);

public record ReconciliationEntryDto(
    string ProviderMessageSid,
    string Outcome,
    string? ProviderStatus,
    string? EShopStatus,
    int? NotificationId,
    int? OrderId);

public record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderCount,
    int EShopCount,
    int MatchedCount,
    int MissingInEShopCount,
    int MissingAtProviderCount,
    IReadOnlyList<ReconciliationEntryDto> Entries);

/// <summary>Mapping helpers from domain objects to API DTOs. Destination phone numbers are never mapped out.</summary>
public static class SmsNotificationMapper
{
    public static string? GetBuyerId(this ClaimsPrincipal user) => user.Identity?.Name;

    public static ContactNumberDto ToDto(this ContactNumber contactNumber) =>
        new(contactNumber.Id, contactNumber.PhoneNumber, contactNumber.CreatedDate);

    public static NotificationDto ToDto(this OrderNotification n) =>
        new(n.Id, n.OrderId, n.Type.ToString(), n.Status, n.Body, n.ContentDisposed,
            n.ProviderMessageSid, n.IsScheduled, n.ScheduledSendAt, n.ProviderErrorCode, n.CreatedDate);

    public static OrderDto ToDto(this Order order) =>
        new(order.Id, order.Status.ToString(), order.OrderDate, order.Total(),
            order.OrderItems.Select(i => new OrderLineDto(
                i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList());

    public static OrderWithNotificationsDto ToDto(this OrderWithNotifications ordersWithNotifications) =>
        new(ordersWithNotifications.Order.ToDto(),
            ordersWithNotifications.Notifications.Select(n => n.ToDto()).ToList());

    public static ReconciliationResponse ToResponse(this ReconciliationReport report) =>
        new(report.From, report.To, report.ProviderCount, report.EShopCount,
            report.MatchedCount, report.MissingInEShopCount, report.MissingAtProviderCount,
            report.Entries.Select(e => new ReconciliationEntryDto(
                e.Sid, e.Outcome.ToString(), e.ProviderStatus, e.EShopStatus, e.NotificationId, e.OrderId)).ToList());
}
