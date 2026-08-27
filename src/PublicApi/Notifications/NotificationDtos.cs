using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class RegisterContactNumberRequest
{
    [Required]
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);
public sealed record ContactNumberResponse(int ContactNumberId, string PhoneNumber, DateTimeOffset CreatedAt);

public sealed class PlaceOrderRequest
{
    [Required, MinLength(1)]
    public List<PlaceOrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class PlaceOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; set; }
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public sealed record PlaceOrderResponse(int OrderId);
public sealed record OrderTransitionResponse(int OrderId, string Status);

public sealed record MyOrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    string Status,
    decimal Total,
    IReadOnlyList<MyOrderItemResponse> Items,
    IReadOnlyList<NotificationResponse> Notifications);

public sealed record MyOrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record NotificationResponse(
    int NotificationId,
    int OrderId,
    string Kind,
    string? Content,
    bool ContentDisposed,
    string? ProviderMessageSid,
    string ProviderStatus,
    int? ProviderErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProviderCreatedAt,
    DateTimeOffset? ProviderSentAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? CancellationRequestedAt,
    int? ResendOfNotificationId)
{
    public static NotificationResponse FromEntity(OrderNotification notification) => new(
        notification.Id,
        notification.OrderId,
        notification.Kind.ToString(),
        notification.Content,
        notification.ContentDisposedAt is not null,
        notification.ProviderMessageSid,
        notification.ProviderStatus,
        notification.ProviderErrorCode,
        notification.CreatedAt,
        notification.ProviderCreatedAt,
        notification.ProviderSentAt,
        notification.ScheduledFor,
        notification.CancellationRequestedAt,
        notification.ResendOfNotificationId);
}

public sealed class ResendNotificationRequest
{
    [Required, MaxLength(128)]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record ResendNotificationResponse(int NotificationId);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int ApplicationOnlyCount,
    int ProviderOnlyCount,
    int MatchedCount,
    IReadOnlyList<ReconciliationEntry> Messages)
{
    public static ReconciliationResponse Create(DateTimeOffset from, DateTimeOffset to, IReadOnlyList<ReconciliationEntry> messages) =>
        new(
            from,
            to,
            messages.Count(x => x.InApplication && !x.InProvider),
            messages.Count(x => !x.InApplication && x.InProvider),
            messages.Count(x => x.InApplication && x.InProvider),
            messages);
}
