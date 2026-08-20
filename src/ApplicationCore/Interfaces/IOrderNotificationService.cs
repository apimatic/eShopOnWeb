using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    Task DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListOrderNotificationsAsync(
        string buyerId,
        int orderId,
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

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<ReconciliationProviderMessage> ProviderOnly,
    IReadOnlyList<ReconciliationApplicationMessage> ApplicationOnly);

public record ReconciliationMatch(
    int NotificationId,
    string ProviderMessageSid,
    string ApplicationStatus,
    string ProviderStatus);

public record ReconciliationProviderMessage(
    string ProviderMessageSid,
    string Status,
    DateTimeOffset? DateSent,
    string? Body);

public record ReconciliationApplicationMessage(
    int NotificationId,
    string? ProviderMessageSid,
    string Status,
    DateTimeOffset CreatedAt);
