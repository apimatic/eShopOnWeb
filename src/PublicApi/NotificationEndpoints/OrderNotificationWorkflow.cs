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
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class OrderNotificationWorkflow
{
    private static readonly HashSet<string> FailedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed", "undelivered", "submission_failed"
    };

    private readonly CatalogContext _db;
    private readonly ITwilioMessagingClient _twilio;
    private readonly IUriComposer _uriComposer;
    private readonly TimeProvider _clock;
    private readonly IdempotencyLock _idempotencyLock;
    private readonly ILogger<OrderNotificationWorkflow> _logger;

    public OrderNotificationWorkflow(
        CatalogContext db,
        ITwilioMessagingClient twilio,
        IUriComposer uriComposer,
        TimeProvider clock,
        IdempotencyLock idempotencyLock,
        ILogger<OrderNotificationWorkflow> logger)
    {
        _db = db;
        _twilio = twilio;
        _uriComposer = uriComposer;
        _clock = clock;
        _idempotencyLock = idempotencyLock;
        _logger = logger;
    }

    public async Task<RegisterContactNumberResponse> RegisterContactNumberAsync(
        string buyerId,
        RegisterContactNumberRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber)) throw new WorkflowValidationException("A phone number is required.");

        PhoneNumberValidation validation;
        try
        {
            validation = await _twilio.ValidatePhoneNumberAsync(request.PhoneNumber, request.CountryCode, cancellationToken);
        }
        catch (TwilioProviderException)
        {
            throw new WorkflowProviderUnavailableException("The phone number could not be validated right now.");
        }

        if (!validation.IsValid || validation.CanonicalPhoneNumber is null)
            throw new WorkflowValidationException("Twilio does not consider this a valid destination.", validation.Errors);

        var existing = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.BuyerId == buyerId && x.PhoneNumber == validation.CanonicalPhoneNumber,
            cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsActive)
            {
                existing.Reactivate(_clock.GetUtcNow());
                await _db.SaveChangesAsync(cancellationToken);
            }
            return new RegisterContactNumberResponse(existing.Id, existing.PhoneNumber);
        }

        var contact = new ContactNumber(buyerId, validation.CanonicalPhoneNumber, _clock.GetUtcNow());
        _db.ContactNumbers.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        return new RegisterContactNumberResponse(contact.Id, contact.PhoneNumber);
    }

    public async Task<IReadOnlyList<ContactNumberDto>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken) =>
        await _db.ContactNumbers.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .Select(x => new ContactNumberDto(x.Id, x.PhoneNumber, x.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
            x => x.Id == contactNumberId && x.BuyerId == buyerId && x.DeletedAt == null,
            cancellationToken);
        if (contact is null) return false;

        var pending = await _db.OrderNotifications
            .Where(x => x.ContactNumberId == contact.Id &&
                        x.Kind == NotificationKind.DeliveryFollowUp &&
                        x.ProviderMessageId != null &&
                        x.ProviderStatus == "scheduled")
            .ToListAsync(cancellationToken);

        foreach (var notification in pending)
        {
            try
            {
                var provider = await _twilio.CancelMessageAsync(notification.ProviderMessageId!, cancellationToken);
                notification.RecordProviderState(provider, _clock.GetUtcNow());
            }
            catch (TwilioProviderException)
            {
                throw new WorkflowProviderUnavailableException("A pending message could not be cancelled; the number was not removed.");
            }
        }

        contact.Delete(_clock.GetUtcNow());
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new WorkflowValidationException("At least one catalog item is required.");
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw new WorkflowValidationException("Catalog item ids and quantities must be positive.");
        if (request.Items.GroupBy(x => x.CatalogItemId).Any(x => x.Count() > 1))
            throw new WorkflowValidationException("Each catalog item may appear only once.");

        var ids = request.Items.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
            throw new WorkflowValidationException("One or more catalog items do not exist.");

        var quantities = request.Items.ToDictionary(x => x.CatalogItemId, x => x.Quantity);
        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, _uriComposer.ComposePicUri(item.PictureUri)),
            item.Price,
            quantities[item.Id])).ToList();

        var address = request.ShippingAddress is null
            ? new Address("Not provided", "Not provided", string.Empty, "Not provided", "Not provided")
            : new Address(
                request.ShippingAddress.Street,
                request.ShippingAddress.City,
                request.ShippingAddress.State,
                request.ShippingAddress.Country,
                request.ShippingAddress.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var notifications = await SendToActiveContactsAsync(
            order,
            NotificationKind.OrderPlaced,
            $"Your eShop order #{order.Id} has been placed.",
            null,
            cancellationToken);
        return new PlaceOrderResponse(order.Id, notifications.Select(x => x.Id).ToArray());
    }

    public async Task<OrderTransitionResponse?> DispatchAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return null;
        if (order.Status == OrderStatus.Cancelled) throw new WorkflowConflictException("A cancelled order cannot be dispatched.");
        if (order.Status == OrderStatus.Dispatched)
        {
            return new OrderTransitionResponse(order.Id, order.Status.ToString(), Array.Empty<int>());
        }

        order.Dispatch(_clock.GetUtcNow());
        await _db.SaveChangesAsync(cancellationToken);

        var created = new List<OrderNotification>();
        created.AddRange(await SendToActiveContactsAsync(
            order,
            NotificationKind.OrderDispatched,
            $"Your eShop order #{order.Id} has been dispatched and is on its way.",
            null,
            cancellationToken));
        created.AddRange(await SendToActiveContactsAsync(
            order,
            NotificationKind.DeliveryFollowUp,
            $"How did delivery of your eShop order #{order.Id} go?",
            _clock.GetUtcNow().AddDays(3),
            cancellationToken));

        return new OrderTransitionResponse(order.Id, order.Status.ToString(), created.Select(x => x.Id).ToArray());
    }

    public async Task<OrderTransitionResponse?> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) return null;
        if (order.Status == OrderStatus.Cancelled)
            return new OrderTransitionResponse(order.Id, order.Status.ToString(), Array.Empty<int>());

        var pending = await _db.OrderNotifications
            .Where(x => x.OrderId == order.Id && x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderMessageId != null)
            .ToListAsync(cancellationToken);
        foreach (var notification in pending)
        {
            await TryCancelScheduledAsync(notification, cancellationToken);
        }

        order.Cancel(_clock.GetUtcNow());
        await _db.SaveChangesAsync(cancellationToken);
        var created = await SendToActiveContactsAsync(
            order,
            NotificationKind.OrderCancelled,
            $"Your eShop order #{order.Id} has been cancelled.",
            null,
            cancellationToken);
        return new OrderTransitionResponse(order.Id, order.Status.ToString(), created.Select(x => x.Id).ToArray());
    }

    public async Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var ids = orders.Select(x => x.Id).ToArray();
        var notifications = await _db.OrderNotifications.Where(x => ids.Contains(x.OrderId)).ToListAsync(cancellationToken);
        await RefreshProviderStatesAsync(notifications, cancellationToken);

        return orders.Select(order => new OrderDto(
            order.Id,
            order.OrderDate,
            order.Status.ToString(),
            order.Total(),
            notifications.Where(x => x.OrderId == order.Id).OrderBy(x => x.Id).Select(ToDto).ToArray())).ToArray();
    }

    public async Task<IReadOnlyList<NotificationDto>?> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        if (!await _db.Orders.AnyAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken)) return null;
        var notifications = await _db.OrderNotifications.Where(x => x.OrderId == orderId).OrderBy(x => x.Id).ToListAsync(cancellationToken);
        await RefreshProviderStatesAsync(notifications, cancellationToken);
        return notifications.Select(ToDto).ToArray();
    }

    public async Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            throw new WorkflowValidationException("An idempotency key of at most 200 characters is required.");

        var gate = _idempotencyLock.For($"{notificationId}:{idempotencyKey}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _db.OrderNotifications.SingleOrDefaultAsync(
                x => x.OriginalNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (existing is not null) return existing.Id;

            var original = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
            if (original is null) return null;
            await RefreshProviderStatesAsync(new[] { original }, cancellationToken);
            if (!FailedStatuses.Contains(original.ProviderStatus))
                throw new WorkflowConflictException("Only a failed or undelivered notification can be resent.");
            if (original.Content is null)
                throw new WorkflowConflictException("Disposed message content cannot be resent.");

            var contact = await _db.ContactNumbers.SingleOrDefaultAsync(
                x => x.Id == original.ContactNumberId && x.BuyerId == original.BuyerId && x.DeletedAt == null,
                cancellationToken);
            if (contact is null)
                throw new WorkflowConflictException("The destination is no longer registered.");

            var resend = new OrderNotification(
                original.OrderId,
                original.BuyerId,
                original.ContactNumberId,
                NotificationKind.Resend,
                original.Content,
                _clock.GetUtcNow(),
                originalNotificationId: original.Id,
                idempotencyKey: idempotencyKey);
            _db.OrderNotifications.Add(resend);
            try
            {
                await _db.SaveChangesAsync(cancellationToken); // Reserve the key before the external side effect.
            }
            catch (DbUpdateException)
            {
                _db.Entry(resend).State = EntityState.Detached;
                existing = await _db.OrderNotifications.AsNoTracking().SingleOrDefaultAsync(
                    x => x.OriginalNotificationId == notificationId && x.IdempotencyKey == idempotencyKey,
                    cancellationToken);
                if (existing is not null) return existing.Id;
                throw;
            }
            await SubmitAsync(resend, contact.PhoneNumber, cancellationToken);
            return resend.Id;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _db.OrderNotifications.SingleOrDefaultAsync(x => x.Id == notificationId, cancellationToken);
        if (notification is null) return false;
        if (notification.ContentDeletedAt is not null) return true;

        if (notification.ProviderMessageId is not null)
        {
            ProviderMessage provider;
            try
            {
                provider = await _twilio.RedactMessageAsync(notification.ProviderMessageId, cancellationToken);
            }
            catch (TwilioProviderException)
            {
                throw new WorkflowProviderUnavailableException("Twilio did not confirm content disposal.");
            }
            notification.RecordProviderState(provider, _clock.GetUtcNow());
        }

        notification.DisposeContent(_clock.GetUtcNow());
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from > to) throw new WorkflowValidationException("The from instant must not be later than to.");

        IReadOnlyList<ProviderMessage> provider;
        try
        {
            provider = await _twilio.ListMessagesAsync(from, to, cancellationToken);
        }
        catch (TwilioProviderException)
        {
            throw new WorkflowProviderUnavailableException("Twilio reconciliation is temporarily unavailable.");
        }

        var providerIds = provider.Select(x => x.Id).ToArray();
        var localInRange = await _db.OrderNotifications
            .Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
            .ToListAsync(cancellationToken);
        var matches = await _db.OrderNotifications
            .Where(x => x.ProviderMessageId != null && providerIds.Contains(x.ProviderMessageId))
            .ToListAsync(cancellationToken);
        var localBySid = matches.ToDictionary(x => x.ProviderMessageId!, StringComparer.Ordinal);
        var providerBySid = provider.ToDictionary(x => x.Id, StringComparer.Ordinal);

        var entries = provider.Select(message =>
        {
            localBySid.TryGetValue(message.Id, out var local);
            return new ReconciliationEntry(
                message.Id,
                local?.Id,
                local is null ? "provider_only" : "matched",
                local?.ProviderStatus,
                message.Status,
                message.DateSent);
        }).ToList();

        entries.AddRange(localInRange
            .Where(x => x.ProviderMessageId is null || !providerBySid.ContainsKey(x.ProviderMessageId))
            .Select(x => new ReconciliationEntry(
                x.ProviderMessageId ?? $"local:{x.Id}",
                x.Id,
                "eshop_only",
                x.ProviderStatus,
                null,
                null)));

        return new ReconciliationResponse(from, to, entries.OrderBy(x => x.ProviderDateSent).ThenBy(x => x.NotificationId).ToArray());
    }

    private async Task<IReadOnlyList<OrderNotification>> SendToActiveContactsAsync(
        Order order,
        NotificationKind kind,
        string content,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var contacts = await _db.ContactNumbers
            .Where(x => x.BuyerId == order.BuyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var result = new List<OrderNotification>();
        foreach (var contact in contacts)
        {
            var notification = new OrderNotification(order.Id, order.BuyerId, contact.Id, kind, content, _clock.GetUtcNow(), sendAt);
            _db.OrderNotifications.Add(notification);
            await _db.SaveChangesAsync(cancellationToken);
            await SubmitAsync(notification, contact.PhoneNumber, cancellationToken);
            result.Add(notification);
        }
        return result;
    }

    private async Task SubmitAsync(OrderNotification notification, string destination, CancellationToken cancellationToken)
    {
        try
        {
            var provider = await _twilio.SendMessageAsync(destination, notification.Content!, notification.ScheduledFor, cancellationToken);
            notification.RecordProviderState(provider, _clock.GetUtcNow());
        }
        catch (TwilioProviderException ex)
        {
            notification.RecordSubmissionFailure(ex.ErrorCode, ex.Message, _clock.GetUtcNow());
            _logger.LogWarning("Twilio did not accept notification {NotificationId} for order {OrderId}.", notification.Id, notification.OrderId);
        }
        catch (HttpRequestException)
        {
            notification.RecordSubmissionFailure(null, "Twilio was unreachable.", _clock.GetUtcNow());
            _logger.LogWarning("Twilio was unreachable for notification {NotificationId} on order {OrderId}.", notification.Id, notification.OrderId);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            notification.RecordSubmissionFailure(null, "Twilio timed out.", _clock.GetUtcNow());
            _logger.LogWarning("Twilio timed out for notification {NotificationId} on order {OrderId}.", notification.Id, notification.OrderId);
        }
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task TryCancelScheduledAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var current = await _twilio.FetchMessageAsync(notification.ProviderMessageId!, cancellationToken);
            notification.RecordProviderState(current, _clock.GetUtcNow());
            if (string.Equals(current.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            {
                var cancelled = await _twilio.CancelMessageAsync(notification.ProviderMessageId!, cancellationToken);
                notification.RecordProviderState(cancelled, _clock.GetUtcNow());
            }
        }
        catch (TwilioProviderException)
        {
            notification.RecordSubmissionFailure(notification.ProviderErrorCode, "Twilio could not confirm scheduled-message cancellation.", _clock.GetUtcNow());
            _logger.LogWarning("Twilio could not cancel follow-up notification {NotificationId} for order {OrderId}.", notification.Id, notification.OrderId);
        }
    }

    private async Task RefreshProviderStatesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var notification in notifications.Where(x => x.ProviderMessageId is not null))
        {
            try
            {
                var current = await _twilio.FetchMessageAsync(notification.ProviderMessageId!, cancellationToken);
                notification.RecordProviderState(current, _clock.GetUtcNow());
                changed = true;
            }
            catch (TwilioProviderException)
            {
                // A read remains useful with the last state persisted locally.
            }
        }
        if (changed) await _db.SaveChangesAsync(cancellationToken);
    }

    private static NotificationDto ToDto(OrderNotification x) => new(
        x.Id,
        x.OrderId,
        x.Kind.ToString(),
        x.Content,
        x.ProviderMessageId,
        x.ProviderStatus,
        x.ProviderErrorCode,
        x.ProviderErrorMessage,
        x.CreatedAt,
        x.ScheduledFor,
        x.ProviderDateSent,
        x.ContentDeletedAt,
        x.OriginalNotificationId);
}

public sealed class WorkflowValidationException : Exception
{
    public WorkflowValidationException(string message, IReadOnlyList<string>? details = null) : base(message) => Details = details ?? Array.Empty<string>();
    public IReadOnlyList<string> Details { get; }
}

public sealed class WorkflowConflictException : Exception
{
    public WorkflowConflictException(string message) : base(message) { }
}

public sealed class WorkflowProviderUnavailableException : Exception
{
    public WorkflowProviderUnavailableException(string message) : base(message) { }
}
