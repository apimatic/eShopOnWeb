using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class NotificationOperationsService : INotificationOperationsService
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ResendIdempotencyRecord> _idempotencyRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsGateway _smsGateway;
    private readonly IAppLogger<NotificationOperationsService> _logger;

    public NotificationOperationsService(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ResendIdempotencyRecord> idempotencyRepository,
        IRepository<Order> orderRepository,
        IRepository<ContactNumber> contactNumberRepository,
        ISmsGateway smsGateway,
        IAppLogger<NotificationOperationsService> logger)
    {
        _notificationRepository = notificationRepository;
        _idempotencyRepository = idempotencyRepository;
        _orderRepository = orderRepository;
        _contactNumberRepository = contactNumberRepository;
        _smsGateway = smsGateway;
        _logger = logger;
    }

    public async Task<ResendResult?> ResendAsync(int notificationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        // Idempotency: a repeat under the same key returns the earlier result without sending again.
        var priorRecord = await _idempotencyRepository.FirstOrDefaultAsync(
            new ResendIdempotencyByKeySpecification(idempotencyKey), cancellationToken);
        if (priorRecord is not null)
        {
            return new ResendResult(priorRecord.ResultNotificationId, Replayed: true);
        }

        var origin = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (origin is null)
        {
            return null;
        }

        // Bring the origin's outcome up to date so we only resend something that did not reach the shopper.
        if (origin.ProviderMessageId is not null && !NotificationMapping.IsTerminal(origin.Status))
        {
            try
            {
                var refreshed = await _smsGateway.GetMessageStateAsync(origin.ProviderMessageId, cancellationToken);
                origin.ApplyProviderState(refreshed.Status, refreshed.ProviderStatusRaw, refreshed.ErrorCode,
                    refreshed.ErrorMessage, refreshed.SentAt);
                await _notificationRepository.UpdateAsync(origin, cancellationToken);
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh notification {NotificationId} before resend.", origin.Id);
            }
        }

        if (!IsResendable(origin.Status))
        {
            throw new NotificationNotResendableException(
                $"Notification {origin.Id} has status {origin.Status} and did not need re-sending.");
        }

        if (string.IsNullOrEmpty(origin.ToNumber))
        {
            throw new NotificationNotResendableException(
                $"Notification {origin.Id} has no destination on file, so there is nothing to re-send.");
        }

        // A number that has since been removed must never be messaged again.
        var registered = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(origin.BuyerId), cancellationToken);
        if (registered.All(c => c.PhoneNumber != origin.ToNumber))
        {
            throw new NotificationNotResendableException(
                $"The destination for notification {origin.Id} is no longer on file, so nothing is sent to it.");
        }

        // Rebuild the body from the order + type (never resurrect content that was disposed of).
        var order = await _orderRepository.GetByIdAsync(origin.OrderId, cancellationToken);
        var body = order is not null
            ? NotificationMessages.For(origin.Type, order)
            : origin.Body ?? "eShopOnWeb: an update about your order.";

        var resend = new OrderNotification(origin.OrderId, origin.BuyerId, origin.Type, body, origin.ToNumber);
        resend.MarkAsResendOf(origin.Id);
        await _notificationRepository.AddAsync(resend, cancellationToken);

        try
        {
            var state = await _smsGateway.SendAsync(new SmsMessageRequest(origin.ToNumber, body), cancellationToken);
            resend.RecordAccepted(state.ProviderMessageId, state.Status, state.ProviderStatusRaw,
                state.ErrorCode, state.ErrorMessage, state.SentAt);
        }
        catch (Exception ex)
        {
            resend.RecordSendError(ex.Message);
            _logger.LogWarning("Resend of notification {OriginId} could not be sent.", origin.Id);
        }

        await _notificationRepository.UpdateAsync(resend, cancellationToken);

        await _idempotencyRepository.AddAsync(new ResendIdempotencyRecord(idempotencyKey, origin.Id, resend.Id), cancellationToken);

        return new ResendResult(resend.Id, Replayed: false);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null)
        {
            return false;
        }

        // The text must no longer be retrievable from the provider either — so redact there first.
        // If the provider redaction fails we do NOT claim success: let it surface rather than leave the
        // content live at the provider while telling the caller it is gone.
        if (!string.IsNullOrEmpty(notification.ProviderMessageId))
        {
            await _smsGateway.RedactContentAsync(notification.ProviderMessageId, cancellationToken);
        }

        notification.MarkContentDisposed();
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        _logger.LogInformation("Disposed content of notification {NotificationId}.", notification.Id);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's sending number's messages over the range directly.
        var providerMessages = await _smsGateway.ListOutboundMessagesAsync(from, to, cancellationToken);
        var providerBySid = providerMessages
            .GroupBy(m => m.Sid)
            .ToDictionary(g => g.Key, g => g.First());

        // The eShop side: notifications we handed to the provider, sent within the range.
        var localWithSid = await _notificationRepository.ListAsync(new NotificationsWithProviderMessageSpecification(), cancellationToken);
        var localBySid = localWithSid
            .Where(n => n.ProviderMessageId is not null)
            .GroupBy(n => n.ProviderMessageId!)
            .ToDictionary(g => g.Key, g => g.First());
        var localSentInRange = localWithSid
            .Where(n => n.SentAt.HasValue && n.SentAt.Value >= from && n.SentAt.Value <= to)
            .ToList();

        var entries = new List<ReconciliationEntry>();
        var matched = 0;
        var providerOnly = 0;

        foreach (var providerMessage in providerMessages)
        {
            if (localBySid.TryGetValue(providerMessage.Sid, out var local))
            {
                matched++;
                entries.Add(new ReconciliationEntry(
                    providerMessage.Sid, "Matched", providerMessage.RawStatus, local.Id, local.Status.ToString(),
                    local.OrderId, providerMessage.ErrorCode, providerMessage.DateSent));
            }
            else
            {
                providerOnly++;
                entries.Add(new ReconciliationEntry(
                    providerMessage.Sid, "ProviderOnly", providerMessage.RawStatus, null, null,
                    null, providerMessage.ErrorCode, providerMessage.DateSent));
            }
        }

        var eShopOnly = 0;
        foreach (var local in localSentInRange)
        {
            if (providerBySid.ContainsKey(local.ProviderMessageId!))
            {
                continue; // already reported as Matched
            }

            eShopOnly++;
            entries.Add(new ReconciliationEntry(
                local.ProviderMessageId!, "EShopOnly", null, local.Id, local.Status.ToString(),
                local.OrderId, local.ErrorCode, local.SentAt));
        }

        var report = new ReconciliationReport(
            from, to,
            ProviderCount: providerMessages.Count,
            EShopCount: matched + eShopOnly,
            MatchedCount: matched,
            ProviderOnlyCount: providerOnly,
            EShopOnlyCount: eShopOnly,
            Entries: entries);

        _logger.LogInformation("Reconciled {ProviderCount} provider message(s) against eShop over the range.",
            providerMessages.Count);
        return report;
    }

    private static bool IsResendable(NotificationDeliveryStatus status) => status switch
    {
        NotificationDeliveryStatus.Failed => true,
        NotificationDeliveryStatus.Undelivered => true,
        NotificationDeliveryStatus.SendError => true,
        _ => false
    };
}
