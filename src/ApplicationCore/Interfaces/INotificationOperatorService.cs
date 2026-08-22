using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface INotificationOperatorService
{
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<ReconciledMessage> Matched { get; init; } = Array.Empty<ReconciledMessage>();
    public IReadOnlyList<ReconciledMessage> ProviderOnly { get; init; } = Array.Empty<ReconciledMessage>();
    public IReadOnlyList<ReconciledMessage> EShopOnly { get; init; } = Array.Empty<ReconciledMessage>();
}

public class ReconciledMessage
{
    public int? NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string? EShopStatus { get; init; }
    public string? ProviderStatus { get; init; }
    public string? Kind { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
}
