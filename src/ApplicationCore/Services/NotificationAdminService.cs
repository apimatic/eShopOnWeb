using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationAdminService : INotificationAdminService
{
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<NotificationResend> _resends;
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ITwilioMessagingClient _messagingClient;
    private readonly IOrderNotificationDispatcher _dispatcher;
    private readonly IAppLogger<NotificationAdminService> _logger;

    public NotificationAdminService(
        IRepository<OrderNotification> notifications,
        IRepository<NotificationResend> resends,
        IRepository<ContactNumber> contactNumbers,
        ITwilioMessagingClient messagingClient,
        IOrderNotificationDispatcher dispatcher,
        IAppLogger<NotificationAdminService> logger)
    {
        _notifications = notifications;
        _resends = resends;
        _contactNumbers = contactNumbers;
        _messagingClient = messagingClient;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task<OrderNotification> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.");
        }

        var source = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        var existing = await _resends.FirstOrDefaultAsync(
            new NotificationResendByKeySpecification(notificationId, idempotencyKey),
            cancellationToken);
        if (existing != null && existing.ResultNotificationId > 0)
        {
            var previous = await _notifications.GetByIdAsync(existing.ResultNotificationId, cancellationToken);
            if (previous != null)
            {
                return previous;
            }
        }

        if (source.HasReachedShopper())
        {
            throw new InvalidOperationException("The original message already reached the shopper.");
        }

        if (source.ContentDisposed || string.IsNullOrEmpty(source.Body))
        {
            throw new InvalidOperationException("The original message content has been disposed and cannot be resent.");
        }

        var destination = await ResolveActiveDestinationAsync(source, cancellationToken);
        if (destination == null)
        {
            throw new InvalidOperationException("The destination number is no longer on file for this shopper.");
        }

        if (existing == null)
        {
            existing = new NotificationResend(notificationId, idempotencyKey);
            await _resends.AddAsync(existing, cancellationToken);
        }

        var resent = await _dispatcher.SendToContactAsync(
            source.OrderId,
            source.BuyerId,
            source.Kind,
            source.Body,
            destination,
            source.Id,
            cancellationToken);

        existing.AssignResult(resent.Id);
        await _resends.UpdateAsync(existing, cancellationToken);
        return resent;
    }

    public async Task DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification was not found.");

        if (!string.IsNullOrEmpty(notification.ProviderMessageSid))
        {
            var updated = await _messagingClient.UpdateMessageAsync(
                notification.ProviderMessageSid,
                body: string.Empty,
                status: null,
                cancellationToken);
            notification.ApplyProviderState(updated.Status, updated.ErrorCode, updated.ErrorMessage);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The 'to' timestamp must be on or after 'from'.");
        }

        var fromNumber = _messagingClient.FromNumber;
        if (string.IsNullOrWhiteSpace(fromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }

        var providerMessages = await _messagingClient.ListMessagesAsync(fromNumber, from, to, cancellationToken);
        var providerBySid = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var localInRange = await _notifications.ListAsync(new NotificationsCreatedBetweenSpecification(from, to), cancellationToken);
        var localByProviderSid = providerBySid.Count == 0
            ? new List<OrderNotification>()
            : await _notifications.ListAsync(new NotificationsByProviderSidsSpecification(providerBySid.Keys), cancellationToken);

        var locals = localInRange.Concat(localByProviderSid)
            .GroupBy(n => n.Id)
            .Select(g => g.First())
            .ToList();

        var matched = new List<ReconciliationMatch>();
        var applicationOnly = new List<OrderNotification>();
        var matchedSids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var local in locals)
        {
            if (!string.IsNullOrEmpty(local.ProviderMessageSid) &&
                providerBySid.TryGetValue(local.ProviderMessageSid, out var provider))
            {
                matched.Add(new ReconciliationMatch(local, provider));
                matchedSids.Add(local.ProviderMessageSid);
            }
            else
            {
                applicationOnly.Add(local);
            }
        }

        var providerOnly = providerMessages
            .Where(m => !string.IsNullOrEmpty(m.Sid) && !matchedSids.Contains(m.Sid!))
            .ToList();

        _logger.LogInformation(
            "Reconciliation from {From} to {To}: {Matched} matched, {ProviderOnly} provider-only, {ApplicationOnly} application-only",
            from, to, matched.Count, providerOnly.Count, applicationOnly.Count);

        return new ReconciliationReport(from, to, matched, providerOnly, applicationOnly);
    }

    private async Task<ContactNumber?> ResolveActiveDestinationAsync(OrderNotification source, CancellationToken cancellationToken)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(source.BuyerId), cancellationToken);
        if (source.ContactNumberId.HasValue)
        {
            var byId = numbers.FirstOrDefault(n => n.Id == source.ContactNumberId.Value);
            if (byId != null)
            {
                return byId;
            }
        }

        return numbers.FirstOrDefault(n => n.CanonicalNumber == source.DestinationNumber);
    }
}
