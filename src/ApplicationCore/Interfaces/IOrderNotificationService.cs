using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class ReconciliationEntry
{
    public string MessageSid { get; set; } = string.Empty;
    public int? NotificationId { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public string? LocalStatus { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderMessageCount { get; set; }
    public int LocalMessageCount { get; set; }

    /// <summary>Messages both the provider and eShop know about.</summary>
    public List<ReconciliationEntry> Matched { get; set; } = new();

    /// <summary>Messages the provider recorded from our sending number that eShop has no record of.</summary>
    public List<ReconciliationEntry> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider has no record of in range.</summary>
    public List<ReconciliationEntry> LocalOnly { get; set; } = new();
}

public class ResendResult
{
    private ResendResult(bool succeeded, OrderNotification? notification, bool wasDuplicate, string? error)
    {
        Succeeded = succeeded;
        Notification = notification;
        WasDuplicate = wasDuplicate;
        Error = error;
    }

    public bool Succeeded { get; }
    public OrderNotification? Notification { get; }

    /// <summary>True when the idempotency key was already used; the original resend is returned.</summary>
    public bool WasDuplicate { get; }
    public string? Error { get; }

    public static ResendResult Sent(OrderNotification notification) => new(true, notification, false, null);
    public static ResendResult Duplicate(OrderNotification notification) => new(true, notification, true, null);
    public static ResendResult Failed(string error) => new(false, null, false, error);
}

/// <summary>
/// Orchestrates shopper notifications as orders move. Sending failures never
/// propagate: the underlying order operation always succeeds.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Best-effort refresh of a notification's delivery outcome from the provider.</summary>
    Task RefreshStatusAsync(OrderNotification notification, CancellationToken cancellationToken = default);

    Task<ResendResult> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Dispose of a message's content both locally and at the provider.</summary>
    Task<bool> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
