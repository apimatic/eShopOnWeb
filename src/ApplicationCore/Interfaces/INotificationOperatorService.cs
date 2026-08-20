using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationEntry(
    string? NotificationId,
    string? ProviderMessageSid,
    string Kind,
    string? EshopStatus,
    string? ProviderStatus,
    string Match);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationEntry> Entries,
    int MatchedCount,
    int ProviderOnlyCount,
    int EshopOnlyCount);

public interface INotificationOperatorService
{
    Task<OrderNotification> ResendAsync(
        int notificationId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<OrderNotification> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
