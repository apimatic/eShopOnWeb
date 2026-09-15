using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

// --- Contact numbers -----------------------------------------------------------------------------

public record ContactNumberDto(int ContactNumberId, string PhoneNumber)
{
    public static ContactNumberDto From(ContactNumber c) => new(c.Id, c.PhoneNumber);
}

public class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>Carries the created number's id as a top-level field, as required.</summary>
public record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);

public record ListContactNumbersResponse(IReadOnlyList<ContactNumberDto> ContactNumbers);

// --- Orders --------------------------------------------------------------------------------------

public record OrderLineDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public class CreateOrderRequest
{
    public List<CreateOrderItem> Items { get; set; } = new();
}

public class CreateOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Carries the created order's id as a top-level field, as required.</summary>
public record CreateOrderResponse(int OrderId, string Status, decimal Total, IReadOnlyList<OrderLineDto> Items);

public record OrderStatusResponse(int OrderId, string Status);

public record OrderDto(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderLineDto> Items,
    IReadOnlyList<NotificationDto> Notifications)
{
    public static OrderDto From(Order order, IReadOnlyList<Notification> notifications) => new(
        order.Id,
        order.Status.ToString(),
        order.OrderDate,
        order.Total(),
        order.OrderItems.Select(i => new OrderLineDto(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
        notifications.Select(NotificationDto.From).ToList());
}

public record MyOrdersResponse(IReadOnlyList<OrderDto> Orders);

// --- Notifications -------------------------------------------------------------------------------

/// <summary>
/// The operator/shopper view of one message. Carries the notification id (what operator endpoints
/// act on), the provider's message id and the current delivery outcome. The destination number is
/// deliberately not echoed here.
/// </summary>
public record NotificationDto(
    int NotificationId,
    int OrderId,
    string Kind,
    string DeliveryStatus,
    string? ProviderMessageSid,
    string? ErrorCode,
    bool IsScheduled,
    DateTimeOffset? ScheduledFor,
    bool ContentRedacted,
    int? ResendOfNotificationId,
    DateTimeOffset CreatedAt)
{
    public static NotificationDto From(Notification n) => new(
        n.Id,
        n.OrderId,
        n.Kind.ToString(),
        n.DeliveryStatus,
        n.ProviderMessageSid,
        n.ErrorCode,
        n.IsScheduled,
        n.ScheduledFor,
        n.ContentRedacted,
        n.ResendOfNotificationId,
        n.CreatedAt);
}

public record OrderNotificationsResponse(int OrderId, IReadOnlyList<NotificationDto> Notifications);

/// <summary>Carries the notification id that the resend produced as a top-level field.</summary>
public record ResendNotificationResponse(int NotificationId, string Outcome, string DeliveryStatus, string? ProviderMessageSid);

// --- Reconciliation ------------------------------------------------------------------------------

public record ReconciliationLineDto(
    string? Sid,
    int? NotificationId,
    int? OrderId,
    string? EShopStatus,
    string? ProviderStatus,
    string? ProviderErrorCode,
    DateTimeOffset? ProviderDate)
{
    public static ReconciliationLineDto From(ReconciliationLine l) =>
        new(l.Sid, l.NotificationId, l.OrderId, l.EShopStatus, l.ProviderStatus, l.ProviderErrorCode, l.ProviderDate);
}

public record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    int MatchedCount,
    int OnlyAtProviderCount,
    int OnlyInEShopCount,
    IReadOnlyList<ReconciliationLineDto> Matched,
    IReadOnlyList<ReconciliationLineDto> OnlyAtProvider,
    IReadOnlyList<ReconciliationLineDto> OnlyInEShop)
{
    public static ReconciliationResponse Create(ReconciliationReport r) => new(
        r.From,
        r.To,
        r.FromNumber,
        r.Matched.Count,
        r.OnlyAtProvider.Count,
        r.OnlyInEShop.Count,
        r.Matched.Select(ReconciliationLineDto.From).ToList(),
        r.OnlyAtProvider.Select(ReconciliationLineDto.From).ToList(),
        r.OnlyInEShop.Select(ReconciliationLineDto.From).ToList());
}
