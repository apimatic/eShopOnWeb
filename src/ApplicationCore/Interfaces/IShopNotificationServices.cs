using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PlaceOrderItem
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public class PlaceOrderResult
{
    public PlaceOrderResult(Order order)
    {
        Order = order;
    }

    public Order Order { get; }
}

public class NotificationReconciliationReport
{
    public NotificationReconciliationReport(
        DateTimeOffset from,
        DateTimeOffset to,
        string fromNumber,
        IReadOnlyList<ReconciledMessage> matched,
        IReadOnlyList<SmsMessageSnapshot> providerOnly,
        IReadOnlyList<OrderNotification> localOnly)
    {
        From = from;
        To = to;
        FromNumber = fromNumber;
        Matched = matched;
        ProviderOnly = providerOnly;
        LocalOnly = localOnly;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
    public string FromNumber { get; }
    public IReadOnlyList<ReconciledMessage> Matched { get; }
    public IReadOnlyList<SmsMessageSnapshot> ProviderOnly { get; }
    public IReadOnlyList<OrderNotification> LocalOnly { get; }
}

public class ReconciledMessage
{
    public ReconciledMessage(OrderNotification local, SmsMessageSnapshot provider)
    {
        Local = local;
        Provider = provider;
    }

    public OrderNotification Local { get; }
    public SmsMessageSnapshot Provider { get; }
}

public interface IContactNumberService
{
    Task<ContactNumber> RegisterAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}

public interface IShopOrderService
{
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, Address? shipToAddress, CancellationToken cancellationToken = default);
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<Order> GetBuyerOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, string? buyerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListForOrdersAsync(IEnumerable<int> orderIds, CancellationToken cancellationToken = default);
    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
