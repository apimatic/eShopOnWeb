using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderNotificationView(
    int NotificationId,
    int OrderId,
    NotificationPurpose Purpose,
    string? Body,
    bool ContentRedacted,
    string? ProviderMessageSid,
    string ProviderStatus,
    int? ProviderErrorCode,
    string? ProviderErrorMessage,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset CreatedAt,
    string? SendFailureReason);

public record NotificationReconciliationItem(
    string? ProviderMessageSid,
    int? NotificationId,
    string Source,
    string? ProviderStatus,
    string? LocalStatus,
    DateTimeOffset? ProviderDateCreated,
    DateTimeOffset? LocalCreatedAt);

public record NotificationReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<NotificationReconciliationItem> Matched,
    IReadOnlyList<NotificationReconciliationItem> ProviderOnly,
    IReadOnlyList<NotificationReconciliationItem> LocalOnly);

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task NotifyOrderDispatchedAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task NotifyOrderCancelledAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotificationView>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotificationView>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<OrderNotificationView> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
