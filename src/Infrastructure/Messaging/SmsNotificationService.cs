using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Orchestrates contact numbers and order-lifecycle SMS. Rules enforced here:
/// (1) a send failure never fails the underlying operation; (2) the local record is written before
/// the provider call and completed after; (3) idempotent order transitions gate their outbound
/// messages on a real state change; (4) resend duplicates are prevented by an atomic DB claim.
/// </summary>
public class SmsNotificationService : ISmsNotificationService
{
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromDays(3);
    private const int ReconciliationMaxPages = 50;

    // Provider status values that are terminal — no point refreshing them.
    private static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "delivered", "read", "failed", "undelivered", "canceled" };

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<SmsNotification> _notifications;
    private readonly IRepository<ResendClaim> _resendClaims;
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly ITwilioMessagingClient _twilio;
    private readonly IAppLogger<SmsNotificationService> _logger;

    public SmsNotificationService(
        IRepository<ContactNumber> contactNumbers,
        IRepository<SmsNotification> notifications,
        IRepository<ResendClaim> resendClaims,
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer,
        ITwilioMessagingClient twilio,
        IAppLogger<SmsNotificationService> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _resendClaims = resendClaims;
        _orders = orders;
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
        _twilio = twilio;
        _logger = logger;
    }

    // ---------------- Flow 1: contact numbers ----------------

    public async Task<ContactNumber> RegisterContactNumberAsync(string buyerId, string rawNumber, CancellationToken ct = default)
    {
        // Validate + canonicalize at registration time; reject an unusable destination here rather
        // than at the moment a message fails to go out. Store the provider's canonical form.
        var validation = await _twilio.ValidateNumberAsync(rawNumber, ct);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.E164Number))
            throw new PhoneNumberNotUsableException("The provider does not consider that number a usable destination.");

        var contactNumber = new ContactNumber(buyerId, validation.E164Number!, validation.CountryCode);
        await _contactNumbers.AddAsync(contactNumber, ct);
        _logger.LogInformation("Registered contact number {ContactNumberId} for buyer.", contactNumber.Id);
        return contactNumber;
    }

    public async Task<IReadOnlyList<ContactNumber>> GetContactNumbersAsync(string buyerId, CancellationToken ct = default)
    {
        var list = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        return list;
    }

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken ct = default)
    {
        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), ct);
        if (contactNumber is null)
            return false; // not the caller's, or does not exist

        await _contactNumbers.DeleteAsync(contactNumber, ct);
        _logger.LogInformation("Removed contact number {ContactNumberId} for buyer.", contactNumberId);
        return true;
    }

    // ---------------- Flow 2: order-lifecycle messages ----------------

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
            throw new InvalidOrderException("An order must contain at least one item.");

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity < 1)
                throw new InvalidOrderException($"Quantity for catalog item {line.CatalogItemId} must be at least 1.");

            // Cross-operation invariant: the catalog item id must be one the catalog actually has.
            var catalogItem = await _catalogItems.GetByIdAsync(line.CatalogItemId, ct);
            if (catalogItem is null)
                throw new InvalidOrderException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        await _orders.AddAsync(order, ct);
        _logger.LogInformation("Placed order {OrderId} for buyer.", order.Id);

        await NotifyOrderPlacedAsync(order, ct);
        return order;
    }

    public Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default)
    {
        var body = $"Your eShop order #{order.Id} has been placed. Total: {order.Total():C}. Thank you!";
        return SendToAllBuyerNumbersAsync(order.BuyerId, order.Id, NotificationKind.OrderPlaced, body, ct);
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
            return false;

        var changed = order.MarkDispatched();
        await _orders.UpdateAsync(order, ct);
        if (!changed)
        {
            // Idempotent no-op: already dispatched (or cancelled). Fire nothing.
            _logger.LogInformation("Dispatch for order {OrderId} was a no-op (status {Status}).", orderId, order.Status);
            return true;
        }

        var body = $"Good news! Your eShop order #{order.Id} is on its way.";
        await SendToAllBuyerNumbersAsync(order.BuyerId, order.Id, NotificationKind.OrderDispatched, body, ct);

        // Queue the "how did the delivery go?" follow-up WITH THE PROVIDER for a few days later.
        var followUpBody = $"How did the delivery of your eShop order #{order.Id} go? We'd love your feedback.";
        var sendAt = DateTimeOffset.UtcNow.Add(FollowUpDelay);
        await ScheduleFollowUpForAllBuyerNumbersAsync(order.BuyerId, order.Id, followUpBody, sendAt, ct);

        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null)
            return false;

        var changed = order.MarkCancelled();
        await _orders.UpdateAsync(order, ct);
        if (!changed)
        {
            _logger.LogInformation("Cancel for order {OrderId} was a no-op (already cancelled).", orderId);
            return true;
        }

        // Call off any queued follow-up FIRST, so a cancelled order can never trigger a
        // "how did delivery go?" text.
        var followUps = await _notifications.ListAsync(new CancellableFollowUpForOrderSpecification(orderId), ct);
        foreach (var followUp in followUps)
        {
            try
            {
                var result = await _twilio.CancelScheduledAsync(followUp.ProviderSid!, ct);
                followUp.MarkCanceled(result.Status);
                await _notifications.UpdateAsync(followUp, ct);
                _logger.LogInformation("Cancelled queued follow-up {NotificationId} for order {OrderId}.", followUp.Id, orderId);
            }
            catch (ProviderMessagingException ex)
            {
                // Best effort — do not fail the cancel. Surface it for reconciliation/operator attention.
                _logger.LogWarning("Could not cancel follow-up {NotificationId} for order {OrderId}: {Message}",
                    followUp.Id, orderId, ex.Message);
            }
        }

        var body = $"Your eShop order #{order.Id} has been cancelled. If this is unexpected, please contact support.";
        await SendToAllBuyerNumbersAsync(order.BuyerId, order.Id, NotificationKind.OrderCancelled, body, ct);
        return true;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default)
        => await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);

    public async Task<string?> GetOrderBuyerAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        return order?.BuyerId;
    }

    public async Task<IReadOnlyList<SmsNotification>> GetNotificationsForOrderAsync(int orderId, bool refresh, CancellationToken ct = default)
    {
        var list = await _notifications.ListAsync(new SmsNotificationsByOrderSpecification(orderId), ct);
        if (refresh)
        {
            foreach (var notification in list)
                await RefreshIfNeededAsync(notification, ct);
        }
        return list;
    }

    public Task<SmsNotification?> GetNotificationAsync(int notificationId, CancellationToken ct = default)
        => _notifications.GetByIdAsync(notificationId, ct);

    // ---------------- Flow 3: operator tooling ----------------

    public async Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct = default)
    {
        var original = await _notifications.GetByIdAsync(notificationId, ct);
        if (original is null)
            return new ResendOutcome(ResendStatus.NotFound, null);

        if (!original.CanBeResent)
            return new ResendOutcome(ResendStatus.NotEligible, null);

        // Atomically claim the idempotency key. The claim's key is its PRIMARY KEY, so a second
        // insert under the same key is rejected by the store; we catch that and replay the first result.
        var claim = new ResendClaim(idempotencyKey, notificationId);
        try
        {
            await _resendClaims.AddAsync(claim, ct);
        }
        catch (Exception ex)
        {
            var existing = await _resendClaims.FirstOrDefaultAsync(new ResendClaimByKeySpecification(idempotencyKey), ct);
            if (existing is not null)
            {
                _logger.LogInformation("Resend under key was a replay; returning existing notification {NotificationId}.",
                    existing.ResultingNotificationId);
                return new ResendOutcome(ResendStatus.Replayed, existing.ResultingNotificationId);
            }
            _logger.LogWarning("Resend claim insert failed unexpectedly: {Message}", ex.Message);
            throw;
        }

        // Claim won — actually re-send.
        var body = original.Body ?? "eShop notification (original content unavailable).";
        var resend = SmsNotification.ForSend(original.BuyerId, original.OrderId, NotificationKind.Resend, original.ToNumber, body);
        resend.MarkResendOf(original.Id);
        await _notifications.AddAsync(resend, ct); // local record before the provider call

        try
        {
            var sent = await _twilio.SendAsync(original.ToNumber, body, ct);
            ApplySendResult(resend, sent);
        }
        catch (ProviderMessagingException ex)
        {
            resend.MarkFailed(null, ex.Message);
            _logger.LogWarning("Resend {NotificationId} failed at provider: {Message}", resend.Id, ex.Message);
        }
        await _notifications.UpdateAsync(resend, ct);

        claim.SetResult(resend.Id);
        await _resendClaims.UpdateAsync(claim, ct);
        return new ResendOutcome(ResendStatus.Created, resend.Id);
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken ct = default)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, ct);
        if (notification is null)
            return false;

        if (notification.ProviderSid is not null && !notification.ContentDisposed)
        {
            // Redact at the provider FIRST — if that fails, do not claim disposal (let it surface).
            await _twilio.RedactAsync(notification.ProviderSid, ct);
        }

        notification.DisposeContent();
        await _notifications.UpdateAsync(notification, ct);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        // Ask the provider for OUR sender's messages in the range (filtered provider-side by From).
        var providerPage = await _twilio.ListSentFromNumberAsync(from, to, ReconciliationMaxPages, ct);
        var providerBySid = providerPage.Messages
            .Where(m => !string.IsNullOrEmpty(m.Sid))
            .GroupBy(m => m.Sid!)
            .ToDictionary(g => g.Key, g => g.First());

        // eShop's own record over the SAME clock (provider send-time).
        var eshop = await _notifications.ListAsync(new SmsNotificationsSentBetweenSpecification(from, to), ct);
        var eshopBySid = eshop
            .Where(n => !string.IsNullOrEmpty(n.ProviderSid))
            .GroupBy(n => n.ProviderSid!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntry>();
        int inBoth = 0, providerOnly = 0, eshopOnly = 0;

        foreach (var (sid, providerMsg) in providerBySid)
        {
            var knownToEshop = eshopBySid.TryGetValue(sid, out var eshopNotif);
            if (knownToEshop) inBoth++; else providerOnly++;
            entries.Add(new ReconciliationEntry(
                sid, KnownToProvider: true, KnownToEshop: knownToEshop,
                ProviderStatus: providerMsg.Status,
                ProviderDateSent: providerMsg.DateSent,
                EshopNotificationId: eshopNotif?.Id,
                EshopOutcome: eshopNotif?.Outcome.ToString()));
        }

        foreach (var (sid, notif) in eshopBySid)
        {
            if (providerBySid.ContainsKey(sid))
                continue; // already counted in the in-both pass
            eshopOnly++;
            entries.Add(new ReconciliationEntry(
                sid, KnownToProvider: false, KnownToEshop: true,
                ProviderStatus: null,
                ProviderDateSent: notif.ProviderDateSent,
                EshopNotificationId: notif.Id,
                EshopOutcome: notif.Outcome.ToString()));
        }

        return new ReconciliationReport(from, to, _twilio.FromNumber, inBoth, providerOnly, eshopOnly,
            providerPage.Truncated, entries);
    }

    // ---------------- internals ----------------

    private async Task SendToAllBuyerNumbersAsync(string buyerId, int? orderId, NotificationKind kind, string body, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        if (numbers.Count == 0)
        {
            // A shopper with no number on file is simply not messaged; record that fact.
            var skipped = SmsNotification.NotSent(buyerId, orderId, kind, body);
            await _notifications.AddAsync(skipped, ct);
            _logger.LogInformation("No contact number on file for buyer; {Kind} for order {OrderId} not sent.", kind, orderId);
            return;
        }

        foreach (var number in numbers)
        {
            var notification = SmsNotification.ForSend(buyerId, orderId, kind, number.E164Number, body);
            await _notifications.AddAsync(notification, ct); // local record BEFORE the provider call

            try
            {
                var sent = await _twilio.SendAsync(number.E164Number, body, ct);
                ApplySendResult(notification, sent);
            }
            catch (ProviderMessagingException ex)
            {
                // A message that cannot be sent must never fail the underlying operation.
                notification.MarkFailed(null, ex.Message);
                _logger.LogWarning("{Kind} for order {OrderId} could not be sent (notification {NotificationId}): {Message}",
                    kind, orderId, notification.Id, ex.Message);
            }

            await _notifications.UpdateAsync(notification, ct);
        }
    }

    private async Task ScheduleFollowUpForAllBuyerNumbersAsync(string buyerId, int orderId, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        foreach (var number in numbers)
        {
            var notification = SmsNotification.ForSend(buyerId, orderId, NotificationKind.DeliveryFollowUp, number.E164Number, body);
            await _notifications.AddAsync(notification, ct);

            try
            {
                var scheduled = await _twilio.ScheduleAsync(number.E164Number, body, sendAt, ct);
                notification.MarkScheduled(scheduled.Sid, scheduled.Status, sendAt);
            }
            catch (ProviderMessagingException ex)
            {
                notification.MarkFailed(null, ex.Message);
                _logger.LogWarning("Follow-up for order {OrderId} could not be scheduled (notification {NotificationId}): {Message}",
                    orderId, notification.Id, ex.Message);
            }

            await _notifications.UpdateAsync(notification, ct);
        }
    }

    private static void ApplySendResult(SmsNotification notification, SentMessage sent)
    {
        // The provider may answer 2xx with a status that is not a success — treat failed/undelivered
        // as a failure, never default an absent status to success.
        if (sent.Status is "failed" or "undelivered")
            notification.MarkFailed(sent.ErrorCode, sent.ErrorMessage);
        else
            notification.MarkSent(sent.Sid, sent.Status, sent.DateSent);
    }

    private async Task RefreshIfNeededAsync(SmsNotification notification, CancellationToken ct)
    {
        if (notification.ProviderSid is null || notification.ContentDisposed)
            return;
        if (notification.ProviderStatus is not null && TerminalStatuses.Contains(notification.ProviderStatus))
            return;

        try
        {
            var fresh = await _twilio.FetchAsync(notification.ProviderSid, ct);
            notification.RefreshProviderState(fresh.Status, fresh.ErrorCode, fresh.ErrorMessage, fresh.DateSent);
            await _notifications.UpdateAsync(notification, ct);
        }
        catch (ProviderMessagingException ex)
        {
            _logger.LogWarning("Could not refresh notification {NotificationId}: {Message}", notification.Id, ex.Message);
        }
    }
}
