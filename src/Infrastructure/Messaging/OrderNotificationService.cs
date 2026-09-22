using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Places orders and drives the SMS notifications as an order moves. Notification sends never fail
/// the underlying operation: a send that cannot go out is recorded as a failed notification and the
/// order still progresses. A shopper with no number on file is simply not messaged.
/// </summary>
public class OrderNotificationService : IOrderNotificationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<NotificationResendClaim> _resendClaimRepository;
    private readonly ITwilioMessagingGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderNotificationService> _logger;
    private readonly TwilioSettings _settings;

    public OrderNotificationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IRepository<NotificationResendClaim> resendClaimRepository,
        ITwilioMessagingGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<OrderNotificationService> logger,
        IOptions<TwilioSettings> settings)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _resendClaimRepository = resendClaimRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
            throw new NotificationValidationException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new NotificationValidationException("Every order line must have a quantity of at least 1.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw new NotificationValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var items = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        // The API carries items only; reuse the existing Order model with a minimal ship-to placeholder.
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        var order = new Order(buyerId, shipToAddress, items);
        order = await _orderRepository.AddAsync(order, ct);

        await SafeNotifyAllNumbersAsync(order.Id, buyerId, NotificationKind.OrderPlaced,
            $"Your order #{order.Id} has been placed. Thank you for shopping with eShop!", ct);

        return order.Id;
    }

    public async Task DispatchOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct)
            ?? throw new OrderNotFoundException(orderId);

        // Throws InvalidOrderStateException for an illegal transition (cancelled → dispatched).
        var changed = order.Dispatch();
        if (!changed)
        {
            _logger.LogInformation("Order {OrderId} already dispatched; no notification sent.", orderId);
            return;
        }

        await _orderRepository.UpdateAsync(order, ct);

        await SafeNotifyAllNumbersAsync(order.Id, order.BuyerId, NotificationKind.OrderDispatched,
            $"Good news! Your order #{order.Id} is on its way.", ct);

        // Queue a "how did delivery go?" follow-up WITH THE PROVIDER, a few days out.
        await SafeScheduleFollowUpAsync(order, ct);
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct)
            ?? throw new OrderNotFoundException(orderId);

        var changed = order.Cancel();
        if (!changed)
        {
            _logger.LogInformation("Order {OrderId} already cancelled; no notification sent.", orderId);
            return;
        }

        await _orderRepository.UpdateAsync(order, ct);

        // Call off any pending follow-up FIRST so it can never reach the customer for a cancelled order.
        await CancelPendingFollowUpsAsync(order.Id, ct);

        await SafeNotifyAllNumbersAsync(order.Id, order.BuyerId, NotificationKind.OrderCancelled,
            $"Your order #{order.Id} has been cancelled. If this is unexpected, please contact support.", ct);
    }

    public async Task<ResendOutcome> ResendAsync(int notificationId, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new NotificationValidationException("An idempotency key is required for a resend.");

        var source = await _notificationRepository.GetByIdAsync(notificationId, ct)
            ?? throw new NotificationNotFoundException(notificationId);

        if (string.IsNullOrWhiteSpace(source.Body) || source.ContentRedacted)
            throw new NotificationValidationException("This notification's content has been disposed of and cannot be resent.");

        // Honor deletion: never send to a number the owner has since removed.
        var ownerNumbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByOwnerSpecification(source.OwnerId), ct);
        if (!ownerNumbers.Any(n => n.Value == source.ToNumber))
            throw new NotificationValidationException("The destination number is no longer registered; nothing is sent to it.");

        // Idempotency: claim the key FIRST. A duplicate key is rejected by the primary-key
        // constraint (caught below); a fresh key is admitted. No existence-check, no in-process lock.
        var claim = new NotificationResendClaim(idempotencyKey, notificationId);
        try
        {
            await _resendClaimRepository.AddAsync(claim, ct);
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            // Read the PERSISTED claim without tracking (the failed insert above is still tracked,
            // so GetByIdAsync/FindAsync would return that incomplete in-memory row instead).
            var existing = await _resendClaimRepository.FirstOrDefaultAsync(
                new ResendClaimByKeySpecification(idempotencyKey), ct);
            if (existing?.ResultNotificationId is int existingResult)
                return new ResendOutcome(existingResult, AlreadyProcessed: true);

            // Claimed but not yet completed (a concurrent in-flight request): do not send again.
            _logger.LogInformation("Resend key already in use for notification {NotificationId}; not resending.", notificationId);
            return new ResendOutcome(notificationId, AlreadyProcessed: true);
        }

        var resend = new OrderNotification(source.OwnerId, source.OrderId, NotificationKind.Resend,
            source.ToNumber, source.Body!, resendOfNotificationId: source.Id);
        resend = await _notificationRepository.AddAsync(resend, ct);

        try
        {
            var result = await _gateway.SendAsync(source.ToNumber, source.Body!, ct);
            resend.RecordSent(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, result.DateSent);
        }
        catch (ProviderGatewayException ex)
        {
            resend.RecordSendFailure(ex.Message);
        }
        await _notificationRepository.UpdateAsync(resend, ct);

        claim.Complete(resend.Id);
        await _resendClaimRepository.UpdateAsync(claim, ct);

        _logger.LogInformation("Resent notification {SourceId} as {ResendId}.", source.Id, resend.Id);
        return new ResendOutcome(resend.Id, AlreadyProcessed: false);
    }

    public async Task RedactNotificationContentAsync(int notificationId, CancellationToken ct)
    {
        var notification = await _notificationRepository.GetByIdAsync(notificationId, ct)
            ?? throw new NotificationNotFoundException(notificationId);

        // Dispose of the content at the provider first (this is the operation itself, so a provider
        // failure here surfaces to the caller rather than being swallowed).
        if (!string.IsNullOrWhiteSpace(notification.ProviderMessageSid))
            await _gateway.RedactContentAsync(notification.ProviderMessageSid!, ct);

        notification.RedactContent();
        await _notificationRepository.UpdateAsync(notification, ct);
        _logger.LogInformation("Disposed of content for notification {NotificationId}.", notificationId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var providerListing = await _gateway.ListForFromNumberAsync(from, to, ct);

        // Both sides must line up on the provider's own date_sent. Locally that clock is only known
        // once we have fetched it, so refresh candidate rows (those with a SID but no date_sent yet,
        // created near the window) from the provider before comparing. CreatedAt only *selects*
        // candidates here — it is never the comparison clock.
        var refreshWindowStart = from.AddDays(-(Math.Max(1, _settings.FollowUpDelayDays) + 7));
        var candidates = await _notificationRepository.ListAsync(
            new NotificationsToRefreshSpecification(refreshWindowStart, to, cap: 200), ct);
        foreach (var candidate in candidates)
        {
            try
            {
                var refreshed = await _gateway.FetchAsync(candidate.ProviderMessageSid!, ct);
                candidate.RecordStatus(refreshed.Status, refreshed.ErrorCode, refreshed.ErrorMessage, refreshed.DateSent);
                await _notificationRepository.UpdateAsync(candidate, ct);
            }
            catch (ProviderGatewayException ex)
            {
                _logger.LogWarning("Could not refresh notification {NotificationId} during reconciliation: {Error}", candidate.Id, ex.Message);
            }
        }

        var local = await _notificationRepository.ListAsync(new NotificationsSentInRangeSpecification(from, to), ct);

        var providerSids = new HashSet<string>(providerListing.Messages.Select(m => m.Sid));
        var localBySid = new Dictionary<string, OrderNotification>();
        foreach (var n in local)
            if (n.ProviderMessageSid is { Length: > 0 } sid)
                localBySid[sid] = n;

        var entries = new List<ReconciliationEntry>();

        foreach (var providerMessage in providerListing.Messages)
        {
            if (localBySid.TryGetValue(providerMessage.Sid, out var localMatch))
                entries.Add(new ReconciliationEntry(ReconciliationDisposition.Matched,
                    providerMessage.Sid, providerMessage.Status, localMatch.Id, localMatch.Status));
            else
                entries.Add(new ReconciliationEntry(ReconciliationDisposition.MissingLocally,
                    providerMessage.Sid, providerMessage.Status, null, null));
        }

        foreach (var n in local)
        {
            if (n.ProviderMessageSid is null || !providerSids.Contains(n.ProviderMessageSid))
                entries.Add(new ReconciliationEntry(ReconciliationDisposition.MissingAtProvider,
                    n.ProviderMessageSid, null, n.Id, n.Status));
        }

        return new ReconciliationReport(from, to, providerListing.Messages.Count, local.Count,
            entries, providerListing.Truncated, providerListing.PagesRead);
    }

    // --- helpers ---

    /// <summary>Send one message per registered number; never let a send failure fail the operation.</summary>
    private async Task SafeNotifyAllNumbersAsync(int orderId, string ownerId, NotificationKind kind, string body, CancellationToken ct)
    {
        try
        {
            var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
            foreach (var number in numbers)
            {
                // Local record BEFORE the provider call, completed after it.
                var notification = new OrderNotification(ownerId, orderId, kind, number.Value, body);
                notification = await _notificationRepository.AddAsync(notification, ct);
                try
                {
                    var result = await _gateway.SendAsync(number.Value, body, ct);
                    notification.RecordSent(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, result.DateSent);
                }
                catch (ProviderGatewayException ex)
                {
                    notification.RecordSendFailure(ex.Message);
                }
                await _notificationRepository.UpdateAsync(notification, ct);
            }
        }
        catch (Exception ex)
        {
            // A message that cannot be sent must never fail the underlying operation.
            _logger.LogWarning("Notifying order {OrderId} ({Kind}) failed; the operation still succeeded: {Error}", orderId, kind, ex.Message);
        }
    }

    private async Task SafeScheduleFollowUpAsync(Order order, CancellationToken ct)
    {
        var body = $"How did the delivery of your order #{order.Id} go? We'd love your feedback.";
        var sendAt = DateTimeOffset.UtcNow.AddDays(Math.Max(1, _settings.FollowUpDelayDays));
        try
        {
            var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(order.BuyerId), ct);
            foreach (var number in numbers)
            {
                var followUp = new OrderNotification(order.BuyerId, order.Id, NotificationKind.DeliveryFollowUp,
                    number.Value, body, isScheduledFollowUp: true);
                followUp = await _notificationRepository.AddAsync(followUp, ct);
                try
                {
                    var result = await _gateway.ScheduleAsync(number.Value, body, sendAt, ct);
                    followUp.RecordSent(result.Sid, result.Status, result.ErrorCode, result.ErrorMessage, result.DateSent);
                }
                catch (ProviderGatewayException ex)
                {
                    followUp.RecordSendFailure(ex.Message);
                }
                await _notificationRepository.UpdateAsync(followUp, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Scheduling the follow-up for order {OrderId} failed; dispatch still succeeded: {Error}", order.Id, ex.Message);
        }
    }

    private async Task CancelPendingFollowUpsAsync(int orderId, CancellationToken ct)
    {
        try
        {
            var followUps = await _notificationRepository.ListAsync(new PendingFollowUpsByOrderSpecification(orderId), ct);
            foreach (var followUp in followUps.Where(f => f.IsPendingFollowUp))
            {
                try
                {
                    await _gateway.CancelScheduledAsync(followUp.ProviderMessageSid!, ct);
                    followUp.MarkCanceled();
                    await _notificationRepository.UpdateAsync(followUp, ct);
                }
                catch (ProviderGatewayException ex)
                {
                    _logger.LogWarning("Failed to cancel follow-up {NotificationId} for order {OrderId}: {Error}", followUp.Id, orderId, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Cancelling follow-ups for order {OrderId} failed; cancellation still succeeded: {Error}", orderId, ex.Message);
        }
    }

    private static bool IsDuplicateKey(Exception ex) =>
        ex is DbUpdateException
        || ex is InvalidOperationException     // EF InMemory keyed-store duplicate
        || ex is ArgumentException             // EF InMemory duplicate key (older behaviour)
        || (ex.InnerException is not null && IsDuplicateKey(ex.InnerException));
}
