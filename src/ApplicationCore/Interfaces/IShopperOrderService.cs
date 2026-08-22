using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IShopperOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, CancellationToken cancellationToken = default);

    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order?> GetBuyerOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}

public interface IOrderNotificationQueryService
{
    Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public class NotificationReconciliationReport
{
    public NotificationReconciliationReport(
        DateTimeOffset from,
        DateTimeOffset to,
        string fromNumber,
        IReadOnlyList<ReconciledMessage> matched,
        IReadOnlyList<ReconciledMessage> providerOnly,
        IReadOnlyList<ReconciledMessage> applicationOnly)
    {
        From = from;
        To = to;
        FromNumber = fromNumber;
        Matched = matched;
        ProviderOnly = providerOnly;
        ApplicationOnly = applicationOnly;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
    public string FromNumber { get; }
    public IReadOnlyList<ReconciledMessage> Matched { get; }
    public IReadOnlyList<ReconciledMessage> ProviderOnly { get; }
    public IReadOnlyList<ReconciledMessage> ApplicationOnly { get; }
}

public class ReconciledMessage
{
    public int? NotificationId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string? DeliveryStatus { get; init; }
    public int? OrderId { get; init; }
    public string? Kind { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
}
