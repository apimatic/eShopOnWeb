using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ResendNotificationResult(OrderNotification Notification, bool ReusedExisting);

public record ReconciliationRow(
    string? ProviderSid,
    string Alignment,
    string? ProviderStatus,
    string? ApplicationStatus,
    int? NotificationId,
    string? ProviderDateSent,
    string? ProviderBodyPresent);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    bool Truncated,
    IReadOnlyList<ReconciliationRow> Rows);

public interface INotificationOperatorService
{
    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
