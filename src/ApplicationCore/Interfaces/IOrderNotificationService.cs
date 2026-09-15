using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record NotificationView(
    int NotificationId,
    int OrderId,
    NotificationKind Kind,
    string? ProviderMessageSid,
    string? ProviderStatus,
    int? ProviderErrorCode,
    string? Body,
    bool ContentRedacted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? ProviderDateSent,
    string? SubmitError,
    int? ResendOfNotificationId);

public sealed record ReconciliationItem(
    string? NotificationId,
    string? ProviderMessageSid,
    string Match,
    string? ProviderStatus,
    DateTimeOffset? ProviderDateSent,
    DateTimeOffset? LocalCreatedAt,
    NotificationKind? Kind);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationItem> Items,
    int MatchedCount,
    int ProviderOnlyCount,
    int LocalOnlyCount);

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);
    Task CancelScheduledForContactAsync(int contactNumberId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationView>> ListForOrderAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationView>> ListForBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<int> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
