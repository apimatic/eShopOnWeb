using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class CatalogOrderLine
{
    public CatalogOrderLine(int catalogItemId, int quantity)
    {
        CatalogItemId = catalogItemId;
        Quantity = quantity;
    }

    public int CatalogItemId { get; }
    public int Quantity { get; }
}

public interface IOrderNotificationService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken = default);

    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListBuyerOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListNotificationsForOrdersAsync(IReadOnlyCollection<int> orderIds, CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(System.DateTimeOffset from, System.DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class NotificationReconciliationReport
{
    public NotificationReconciliationReport(
        string fromNumber,
        System.DateTimeOffset from,
        System.DateTimeOffset to,
        IReadOnlyList<ReconciledMessage> matched,
        IReadOnlyList<ReconciledMessage> providerOnly,
        IReadOnlyList<ReconciledMessage> applicationOnly)
    {
        FromNumber = fromNumber;
        From = from;
        To = to;
        Matched = matched;
        ProviderOnly = providerOnly;
        ApplicationOnly = applicationOnly;
    }

    public string FromNumber { get; }
    public System.DateTimeOffset From { get; }
    public System.DateTimeOffset To { get; }
    public IReadOnlyList<ReconciledMessage> Matched { get; }
    public IReadOnlyList<ReconciledMessage> ProviderOnly { get; }
    public IReadOnlyList<ReconciledMessage> ApplicationOnly { get; }
}

public sealed class ReconciledMessage
{
    public ReconciledMessage(string? providerMessageSid, int? notificationId, string? providerStatus, string? applicationStatus)
    {
        ProviderMessageSid = providerMessageSid;
        NotificationId = notificationId;
        ProviderStatus = providerStatus;
        ApplicationStatus = applicationStatus;
    }

    public string? ProviderMessageSid { get; }
    public int? NotificationId { get; }
    public string? ProviderStatus { get; }
    public string? ApplicationStatus { get; }
}
