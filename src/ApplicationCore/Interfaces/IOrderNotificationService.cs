using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderLine(int CatalogItemId, int Quantity);

public record ReconciliationEntry(
    string? NotificationId,
    string? ProviderSid,
    string Match,
    string? ProviderStatus,
    string? ApplicationStatus,
    string? DateSent,
    string? Kind);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Entries);

public interface IOrderNotificationService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken = default);

    Task DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> GetNotificationsAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
