using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends and tracks order SMS notifications. Notification failures never fail
/// the underlying order operation: every send is best-effort and recorded.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the notifications for an order, refreshing non-terminal delivery
    /// outcomes from the provider (the provider cannot call back into this app).
    /// </summary>
    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    Task<ResendNotificationResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content: redacts the body at the provider and
    /// locally, while keeping the record that a message was sent and its outcome.
    /// </summary>
    Task<RedactContentResult> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lines up the provider's own record of messages sent from this
    /// application's configured sending number against local notification records.
    /// </summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

public enum ResendStatus
{
    Resent,
    DuplicateRequest,
    NotFound,
    ContentRedacted,
    ContactNumberRemoved,
    SendFailed
}

public class ResendNotificationResult
{
    public ResendStatus Status { get; set; }
    public OrderNotification? Notification { get; set; }
}

public enum RedactContentStatus
{
    Redacted,
    AlreadyRedacted,
    NotFound,
    ProviderRedactionFailed
}

public class RedactContentResult
{
    public RedactContentStatus Status { get; set; }
    public OrderNotification? Notification { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
    public IReadOnlyList<ReconciliationMatch> Matched { get; set; } = Array.Empty<ReconciliationMatch>();

    /// <summary>Messages the provider knows about (from our sending number) that eShop has no record of.</summary>
    public IReadOnlyList<SmsMessageState> MissingFromLocal { get; set; } = Array.Empty<SmsMessageState>();

    /// <summary>Notifications eShop believes it sent that the provider did not return for the range.</summary>
    public IReadOnlyList<OrderNotification> MissingFromProvider { get; set; } = Array.Empty<OrderNotification>();
}

public class ReconciliationMatch
{
    public OrderNotification? Notification { get; set; }
    public SmsMessageState? ProviderMessage { get; set; }
    public bool StatusMatches { get; set; }
}
