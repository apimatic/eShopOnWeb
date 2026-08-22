using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class NotificationReconciliationEntry
{
    public int? NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string? LocalStatus { get; init; }
    public string? ProviderStatus { get; init; }
    public string Match { get; init; } = "unknown";
}

public class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public IReadOnlyList<NotificationReconciliationEntry> Matched { get; init; } = Array.Empty<NotificationReconciliationEntry>();
    public IReadOnlyList<NotificationReconciliationEntry> ProviderOnly { get; init; } = Array.Empty<NotificationReconciliationEntry>();
    public IReadOnlyList<NotificationReconciliationEntry> LocalOnly { get; init; } = Array.Empty<NotificationReconciliationEntry>();
}

public interface INotificationReconciliationService
{
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
