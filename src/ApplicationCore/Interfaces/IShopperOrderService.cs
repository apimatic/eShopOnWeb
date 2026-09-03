using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PlaceOrderLine(int CatalogItemId, int Quantity);

public sealed record PlaceOrderAddress(string Street, string City, string State, string Country, string ZipCode);

public sealed record NotificationView(
    int NotificationId,
    int OrderId,
    string Kind,
    string Status,
    string? ProviderSid,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    bool ContentRedacted,
    DateTimeOffset CreatedAt);

public sealed record OrderWithNotificationsView(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<NotificationView> Notifications);

public sealed record ReconciliationRow(
    string? ProviderSid,
    string? EshopNotificationId,
    string Match,
    string? ProviderStatus,
    string? EshopStatus,
    string? DateSent);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    bool Truncated,
    IReadOnlyList<ReconciliationRow> Rows);

public interface IShopperContactService
{
    Task<ShopperContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken ct);
    Task<IReadOnlyList<ShopperContactNumber>> ListAsync(string buyerId, CancellationToken ct);
    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken ct);
}

public interface IShopperOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderLine> lines, PlaceOrderAddress shipTo, CancellationToken ct);
    Task DispatchAsync(int orderId, CancellationToken ct);
    Task CancelAsync(int orderId, CancellationToken ct);
    Task<IReadOnlyList<OrderWithNotificationsView>> ListMyOrdersAsync(string buyerId, CancellationToken ct);
    Task<IReadOnlyList<NotificationView>> ListOrderNotificationsAsync(int orderId, string buyerId, bool isAdmin, CancellationToken ct);
    Task<int> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct);
    Task RedactContentAsync(int notificationId, CancellationToken ct);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
