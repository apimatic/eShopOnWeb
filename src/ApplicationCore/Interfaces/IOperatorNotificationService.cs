using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationMessage(
    string? ProviderMessageSid,
    string? Status,
    string? Body,
    string? From,
    string? To,
    string? DateCreated,
    string? DateSent,
    int? LocalNotificationId,
    int? LocalOrderId,
    string Alignment);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationMessage> Messages,
    int MatchedCount,
    int ProviderOnlyCount,
    int LocalOnlyCount);

public interface IOperatorNotificationService
{
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
