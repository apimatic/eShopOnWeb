using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOperatorNotificationService
{
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class NotificationReconciliationReport
{
    public NotificationReconciliationReport(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<ReconciledNotification> matched,
        IReadOnlyList<ReconciledNotification> providerOnly,
        IReadOnlyList<ReconciledNotification> applicationOnly)
    {
        From = from;
        To = to;
        Matched = matched;
        ProviderOnly = providerOnly;
        ApplicationOnly = applicationOnly;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
    public IReadOnlyList<ReconciledNotification> Matched { get; }
    public IReadOnlyList<ReconciledNotification> ProviderOnly { get; }
    public IReadOnlyList<ReconciledNotification> ApplicationOnly { get; }
}

public sealed class ReconciledNotification
{
    public ReconciledNotification(
        int? notificationId,
        string? providerMessageSid,
        string? status,
        string? body,
        string? dateSent,
        string? dateCreated)
    {
        NotificationId = notificationId;
        ProviderMessageSid = providerMessageSid;
        Status = status;
        Body = body;
        DateSent = dateSent;
        DateCreated = dateCreated;
    }

    public int? NotificationId { get; }
    public string? ProviderMessageSid { get; }
    public string? Status { get; }
    public string? Body { get; }
    public string? DateSent { get; }
    public string? DateCreated { get; }
}
