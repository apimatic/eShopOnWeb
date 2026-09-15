using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderItemRequest(int CatalogItemId, int Quantity);

public record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);

public record NotificationView(
    int NotificationId,
    NotificationKind Kind,
    string? ProviderMessageSid,
    string Status,
    string? Body,
    string? ErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? DateSent,
    bool ContentRedacted);

public record OrderItemView(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record OrderView(
    int OrderId,
    OrderStatus Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderItemView> Items,
    IReadOnlyList<NotificationView> Notifications);

public record PlaceOrderResult(int OrderId, OrderStatus Status);

public interface IShopOrderService
{
    Task<PlaceOrderResult> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItemRequest> items,
        ShippingAddressRequest? shipTo,
        CancellationToken cancellationToken = default);

    Task DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}
