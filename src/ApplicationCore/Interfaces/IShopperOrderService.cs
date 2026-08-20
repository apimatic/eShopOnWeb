using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class PlaceOrderItem
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class PlaceOrderResult
{
    public required int OrderId { get; init; }
    public required string Status { get; init; }
    public required IReadOnlyList<NotificationView> Notifications { get; init; }
}

public sealed class ShopperOrderView
{
    public required int OrderId { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset OrderDate { get; init; }
    public required decimal Total { get; init; }
    public required IReadOnlyList<ShopperOrderItemView> Items { get; init; }
    public required IReadOnlyList<NotificationView> Notifications { get; init; }
}

public sealed class ShopperOrderItemView
{
    public required int CatalogItemId { get; init; }
    public required string ProductName { get; init; }
    public required decimal UnitPrice { get; init; }
    public required int Units { get; init; }
}

public sealed class NotificationView
{
    public required int NotificationId { get; init; }
    public required int OrderId { get; init; }
    public required string Kind { get; init; }
    public required string ProviderStatus { get; init; }
    public string? ProviderMessageSid { get; init; }
    public int? ProviderErrorCode { get; init; }
    public string? ProviderErrorMessage { get; init; }
    public string? Body { get; init; }
    public required bool ContentRedacted { get; init; }
    public DateTimeOffset? ScheduledSendAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public int? SourceNotificationId { get; init; }
}

public interface IShopperOrderService
{
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShopperOrderView>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationView>> ListOrderNotificationsAsync(string buyerId, int orderId, bool isAdministrator, CancellationToken cancellationToken = default);
}
