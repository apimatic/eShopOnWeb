using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string FromNumber,
    IReadOnlyList<ReconciliationRow> Matched,
    IReadOnlyList<SmsMessageSnapshot> ProviderOnly,
    IReadOnlyList<OrderNotification> EShopOnly,
    bool Truncated);

public record ReconciliationRow(OrderNotification Local, SmsMessageSnapshot Provider);

public interface IOperatorOrderNotificationService
{
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);

    Task<OrderNotification?> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
