using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IContactNumberService
{
    Task<ContactNumber> RegisterAsync(string buyerId, string rawPhoneNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactNumber>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken = default);
}

public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, bool refreshFromProvider, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> GetForOrdersAsync(IReadOnlyCollection<int> orderIds, bool refreshFromProvider, CancellationToken cancellationToken = default);

    Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<NotificationReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class NotificationReconciliationReport
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public IReadOnlyList<ReconciledNotification> Matched { get; init; } = [];
    public IReadOnlyList<ProviderOnlyMessage> ProviderOnly { get; init; } = [];
    public IReadOnlyList<ReconciledNotification> EshopOnly { get; init; } = [];
}

public sealed class ReconciledNotification
{
    public int? NotificationId { get; init; }
    public int? OrderId { get; init; }
    public string? ProviderMessageSid { get; init; }
    public string? EshopStatus { get; init; }
    public string? ProviderStatus { get; init; }
    public string? Kind { get; init; }
}

public sealed class ProviderOnlyMessage
{
    public string ProviderMessageSid { get; init; } = string.Empty;
    public string? ProviderStatus { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
}
