using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ResendNotificationResult(int NotificationId, bool AlreadyProcessed);

public record ReconciliationMessage(
    string? ProviderMessageSid,
    int? NotificationId,
    string? ProviderStatus,
    string? ApplicationStatus,
    DateTimeOffset? DateSent);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationMessage> Matched,
    IReadOnlyList<ReconciliationMessage> ProviderOnly,
    IReadOnlyList<ReconciliationMessage> ApplicationOnly);

public interface INotificationOperatorService
{
    Task<ResendNotificationResult?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
