using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ReconciliationReport
{
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required string FromNumber { get; init; }
    public required IReadOnlyList<ReconciliationMatch> Matched { get; init; }
    public required IReadOnlyList<SmsMessageSnapshot> ProviderOnly { get; init; }
    public required IReadOnlyList<OrderNotification> EshopOnly { get; init; }
}

public class ReconciliationMatch
{
    public required OrderNotification Notification { get; init; }
    public required SmsMessageSnapshot ProviderMessage { get; init; }
}

public interface IOperatorOrderNotificationService
{
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
