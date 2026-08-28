using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public interface IOrderNotificationService
{
    Task SendOrderEventAsync(Order order, NotificationKind kind, CancellationToken cancellationToken);
    Task SendDispatchNotificationsAsync(Order order, CancellationToken cancellationToken);
    Task CancelScheduledAsync(int? orderId, int? contactNumberId, CancellationToken cancellationToken);
    Task RetryPendingCancellationsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken);
    Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken);
    Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed class OrderNotificationService : IOrderNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly IMessageProvider _provider;

    public OrderNotificationService(CatalogContext db, IMessageProvider provider)
    {
        _db = db;
        _provider = provider;
    }

    public async Task SendOrderEventAsync(Order order, NotificationKind kind,
        CancellationToken cancellationToken)
    {
        var contacts = await ActiveContactsAsync(order.BuyerId, cancellationToken);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(order, contact, kind, MessageBody(kind, order.Id), null,
                null, cancellationToken);
        }
    }

    public async Task SendDispatchNotificationsAsync(Order order, CancellationToken cancellationToken)
    {
        var contacts = await ActiveContactsAsync(order.BuyerId, cancellationToken);
        var scheduledFor = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        foreach (var contact in contacts)
        {
            await CreateAndSendAsync(order, contact, NotificationKind.OrderDispatched,
                MessageBody(NotificationKind.OrderDispatched, order.Id), null, null, cancellationToken);
            await CreateAndSendAsync(order, contact, NotificationKind.DeliveryFollowUp,
                MessageBody(NotificationKind.DeliveryFollowUp, order.Id), scheduledFor, null, cancellationToken);
        }
    }

    public async Task CancelScheduledAsync(int? orderId, int? contactNumberId,
        CancellationToken cancellationToken)
    {
        var query = _db.OrderNotifications.Where(x => x.Kind == NotificationKind.DeliveryFollowUp);
        if (orderId.HasValue) query = query.Where(x => x.OrderId == orderId.Value);
        if (contactNumberId.HasValue) query = query.Where(x => x.ContactNumberId == contactNumberId.Value);

        var scheduled = await query.Where(x => x.ScheduledFor != null &&
                x.ProviderStatus != "canceled" && x.ProviderStatus != "delivered" &&
                x.ProviderStatus != "sent" && x.ProviderStatus != "failed" &&
                x.ProviderStatus != "undelivered")
            .ToListAsync(cancellationToken);

        foreach (var notification in scheduled)
        {
            notification.RequestCancellation();
            await _db.SaveChangesAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid)) continue;

            try
            {
                var state = await _provider.CancelAsync(notification.ProviderMessageSid, cancellationToken);
                notification.RecordProviderState(state);
            }
            catch (ProviderRequestException ex)
            {
                notification.RecordProviderFailure(ex.ProviderCode);
                notification.RequestCancellation();
            }
            catch (HttpRequestException)
            {
                notification.RecordProviderFailure();
                notification.RequestCancellation();
            }
            catch (Exception)
            {
                notification.RecordProviderFailure();
                notification.RequestCancellation();
            }
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }

    public async Task RetryPendingCancellationsAsync(CancellationToken cancellationToken)
    {
        var pending = await _db.OrderNotifications
            .Where(x => x.CancellationRequested && x.ProviderMessageSid != null &&
                x.ProviderStatus != "canceled" && x.ScheduledFor > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var notification in pending)
        {
            try
            {
                notification.RecordProviderState(await _provider.CancelAsync(
                    notification.ProviderMessageSid!, cancellationToken));
            }
            catch (ProviderRequestException ex)
            {
                notification.RecordProviderFailure(ex.ProviderCode);
                notification.RequestCancellation();
            }
            catch (Exception)
            {
                notification.RecordProviderFailure();
                notification.RequestCancellation();
            }
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }

    public async Task<IReadOnlyList<OrderNotification>> GetForOrderAsync(int orderId,
        CancellationToken cancellationToken)
    {
        var notifications = await _db.OrderNotifications
            .Where(x => x.OrderId == orderId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        foreach (var notification in notifications)
        {
            await RefreshAsync(notification, cancellationToken);
        }
        await _db.SaveChangesAsync(CancellationToken.None);
        return notifications;
    }

    public async Task<int?> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var normalizedKey = idempotencyKey.Trim();
        var existing = await _db.NotificationResends.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SourceNotificationId == notificationId &&
                x.IdempotencyKey == normalizedKey, cancellationToken);
        if (existing is not null) return existing.NotificationId;

        var source = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId,
            cancellationToken);
        if (source is null) return null;
        await RefreshAsync(source, cancellationToken);

        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == source.ContactNumberId && x.IsActive, cancellationToken);
        var order = await _db.Orders.SingleAsync(x => x.Id == source.OrderId, cancellationToken);
        if (!CanResend(source, order, contact)) return -1;

        var resend = new OrderNotification(source.OrderId, source.BuyerId, source.ContactNumberId,
            NotificationKind.Resend, source.Body!, null, source.Id);
        _db.OrderNotifications.Add(resend);
        _db.NotificationResends.Add(new NotificationResend(source.Id, resend, normalizedKey));
        await _db.SaveChangesAsync(cancellationToken);

        await SendPersistedAsync(resend, contact!, cancellationToken);
        return resend.Id;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId,
            cancellationToken);
        if (notification is null) return false;
        if (notification.ContentDisposedAt.HasValue) return true;

        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
        {
            var state = await _provider.RedactContentAsync(notification.ProviderMessageSid, cancellationToken);
            notification.RecordProviderState(state);
        }
        notification.DisposeContent();
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var providerMessages = await _provider.ListAsync(from, to, cancellationToken);
        var providerSids = providerMessages.Select(x => x.Sid).ToList();
        var applicationMessages = await _db.OrderNotifications.AsNoTracking()
            .Where(x => x.ProviderMessageSid != null &&
                ((x.CreatedAt >= from && x.CreatedAt <= to) ||
                 (x.ProviderDateSent >= from && x.ProviderDateSent <= to) ||
                 providerSids.Contains(x.ProviderMessageSid)))
            .ToListAsync(cancellationToken);

        var providerBySid = providerMessages.ToDictionary(x => x.Sid, StringComparer.Ordinal);
        var appBySid = applicationMessages.ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var sids = providerBySid.Keys.Union(appBySid.Keys, StringComparer.Ordinal).OrderBy(x => x);
        var entries = new List<ReconciliationEntry>();
        foreach (var sid in sids)
        {
            providerBySid.TryGetValue(sid, out var provider);
            appBySid.TryGetValue(sid, out var app);
            entries.Add(new ReconciliationEntry(sid, app?.Id,
                provider is not null && app is not null ? "matched" : provider is not null ? "provider-only" : "application-only",
                provider?.Status, app?.ProviderStatus, provider?.DateSent, app?.CreatedAt));
        }
        return new ReconciliationResponse(from, to, entries);
    }

    private async Task<List<ContactNumber>> ActiveContactsAsync(string buyerId, CancellationToken cancellationToken) =>
        await _db.ContactNumbers.Where(x => x.BuyerId == buyerId && x.IsActive)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);

    private async Task CreateAndSendAsync(Order order, ContactNumber contact, NotificationKind kind,
        string body, DateTimeOffset? scheduledFor, int? originalNotificationId,
        CancellationToken cancellationToken)
    {
        var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, kind,
            body, scheduledFor, originalNotificationId);
        _db.OrderNotifications.Add(notification);
        await _db.SaveChangesAsync(cancellationToken);
        await SendPersistedAsync(notification, contact, cancellationToken);
    }

    private async Task SendPersistedAsync(OrderNotification notification, ContactNumber contact,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await _provider.SendAsync(contact.CanonicalNumber, notification.Body!,
                notification.ScheduledFor, cancellationToken);
            notification.RecordProviderState(state);
        }
        catch (ProviderRequestException ex)
        {
            notification.RecordProviderFailure(ex.ProviderCode);
        }
        catch (HttpRequestException)
        {
            notification.RecordProviderFailure();
        }
        catch (TaskCanceledException)
        {
            notification.RecordProviderFailure();
        }
        catch (Exception)
        {
            notification.RecordProviderFailure();
        }
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task RefreshAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.ProviderMessageSid)) return;
        try
        {
            var state = await _provider.FetchAsync(notification.ProviderMessageSid, cancellationToken);
            notification.RecordProviderState(state);
        }
        catch (ProviderRequestException)
        {
            // Reporting remains available with the last provider outcome we persisted.
        }
        catch (HttpRequestException)
        {
        }
        catch (Exception)
        {
        }
    }

    private static bool CanResend(OrderNotification source, Order order, ContactNumber? contact)
    {
        if (contact is null || string.IsNullOrWhiteSpace(source.Body)) return false;
        if (source.Kind == NotificationKind.DeliveryFollowUp ||
            source.ProviderStatus.Equals("canceled", StringComparison.OrdinalIgnoreCase)) return false;
        if (source.Kind != NotificationKind.OrderCancelled && order.Status == OrderStatus.Cancelled) return false;
        return source.ProviderStatus.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
            source.ProviderStatus.Equals("undelivered", StringComparison.OrdinalIgnoreCase) ||
            source.ProviderStatus.Equals("provider-error", StringComparison.OrdinalIgnoreCase);
    }

    private static string MessageBody(NotificationKind kind, int orderId) => kind switch
    {
        NotificationKind.OrderPlaced => $"eShopOnWeb: Your order #{orderId} has been placed.",
        NotificationKind.OrderDispatched => $"eShopOnWeb: Your order #{orderId} has been dispatched and is on its way.",
        NotificationKind.DeliveryFollowUp => $"eShopOnWeb: How did delivery of order #{orderId} go?",
        NotificationKind.OrderCancelled => $"eShopOnWeb: Your order #{orderId} has been cancelled.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
