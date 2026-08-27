using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public sealed class OrderNotificationService : IOrderNotificationService
{
    private static readonly HashSet<string> FailedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "failed", "undelivered", "local-failure"
    };

    private readonly IRepository<ContactNumber> _contacts;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly ISmsProvider _sms;
    private readonly IUriComposer _uriComposer;
    private readonly TimeProvider _timeProvider;

    public OrderNotificationService(
        IRepository<ContactNumber> contacts,
        IRepository<OrderNotification> notifications,
        IRepository<Order> orders,
        IReadRepository<CatalogItem> catalogItems,
        ISmsProvider sms,
        IUriComposer uriComposer,
        TimeProvider timeProvider)
    {
        _contacts = contacts;
        _notifications = notifications;
        _orders = orders;
        _catalogItems = catalogItems;
        _sms = sms;
        _uriComposer = uriComposer;
        _timeProvider = timeProvider;
    }

    public async Task<ContactNumberView> RegisterContactNumberAsync(string buyerId, string phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber)) throw new ArgumentException("A phone number is required.");

        var validation = await _sms.ValidatePhoneNumberAsync(phoneNumber, cancellationToken);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.CanonicalNumber))
        {
            throw new ArgumentException("Twilio does not consider this a valid destination.");
        }

        var existing = (await _contacts.ListAsync(new ActiveContactNumbersByBuyerSpec(buyerId), cancellationToken))
            .FirstOrDefault(x => x.Value == validation.CanonicalNumber);
        if (existing is not null) return new ContactNumberView(existing.Id, existing.Value);

        var contact = await _contacts.AddAsync(new ContactNumber(buyerId, validation.CanonicalNumber, Now), cancellationToken);
        return new ContactNumberView(contact.Id, contact.Value);
    }

    public async Task<IReadOnlyList<ContactNumberView>> GetContactNumbersAsync(string buyerId, CancellationToken cancellationToken) =>
        (await _contacts.ListAsync(new ActiveContactNumbersByBuyerSpec(buyerId), cancellationToken))
            .Select(x => new ContactNumberView(x.Id, x.Value)).ToList();

    public async Task<bool> DeleteContactNumberAsync(string buyerId, int contactNumberId, CancellationToken cancellationToken)
    {
        var contact = await _contacts.FirstOrDefaultAsync(new ActiveContactNumberByOwnerSpec(contactNumberId, buyerId), cancellationToken);
        if (contact is null) return false;

        var scheduled = await _notifications.ListAsync(new ScheduledNotificationsByContactSpec(contact.Id), cancellationToken);
        foreach (var notification in scheduled.Where(x => !IsTerminal(x.ProviderStatus)))
        {
            var state = await CancelWithRetryAsync(notification.ProviderMessageSid!, cancellationToken);
            notification.RecordProviderState(state);
            await _notifications.UpdateAsync(notification, cancellationToken);
        }

        contact.Delete(Now);
        await _contacts.UpdateAsync(contact, cancellationToken);
        return true;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items, ShippingAddressInput? address, CancellationToken cancellationToken)
    {
        if (items.Count == 0 || items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw new ArgumentException("At least one catalog item with a positive quantity is required.");

        var lines = items.GroupBy(x => x.CatalogItemId).Select(x => new OrderLineInput(x.Key, x.Sum(y => y.Quantity))).ToList();
        var catalog = await _catalogItems.ListAsync(new CatalogItemsSpecification(lines.Select(x => x.CatalogItemId).ToArray()), cancellationToken);
        if (catalog.Count != lines.Count) throw new ArgumentException("One or more catalog items do not exist.");

        var orderItems = lines.Select(line =>
        {
            var item = catalog.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(
                new CatalogItemOrdered(item.Id, item.Name, _uriComposer.ComposePicUri(item.PictureUri)),
                item.Price,
                line.Quantity);
        }).ToList();

        address ??= new ShippingAddressInput("Not provided", "Not provided", string.Empty, "Not provided", "Not provided");
        var order = new Order(buyerId, new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        await NotifyAllSafelyAsync(order, NotificationKind.OrderPlaced, $"Your eShopOnWeb order #{order.Id} has been placed.", null, cancellationToken);
        return order.Id;
    }

    public async Task<bool> DispatchOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return false;
        if (order.Status == OrderStatus.Cancelled) throw new InvalidOperationException("A cancelled order cannot be dispatched.");
        if (order.Status == OrderStatus.Dispatched) return true;

        order.Dispatch();
        await _orders.UpdateAsync(order, cancellationToken);
        await NotifyAllSafelyAsync(order, NotificationKind.OrderDispatched, $"Your eShopOnWeb order #{order.Id} is on its way.", null, cancellationToken);
        await NotifyAllSafelyAsync(order, NotificationKind.DeliveryFollowUp, $"How did delivery of your eShopOnWeb order #{order.Id} go?", Now.AddDays(3), cancellationToken);
        return true;
    }

    public async Task<bool> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return false;
        if (order.Status == OrderStatus.Cancelled) return true;

        order.Cancel();
        await _orders.UpdateAsync(order, cancellationToken);

        var existing = await _notifications.ListAsync(new NotificationsByOrderSpec(orderId), cancellationToken);
        foreach (var notification in existing.Where(x => x.Kind == NotificationKind.DeliveryFollowUp && x.ProviderMessageSid is not null && !IsTerminal(x.ProviderStatus)))
        {
            try
            {
                var state = await CancelWithRetryAsync(notification.ProviderMessageSid!, cancellationToken);
                notification.RecordProviderState(state);
                await _notifications.UpdateAsync(notification, cancellationToken);
            }
            catch
            {
                // The order transition must survive notification-provider failures.
                notification.RecordFailure();
                await TryUpdateAsync(notification, cancellationToken);
            }
        }

        await NotifyAllSafelyAsync(order, NotificationKind.OrderCancelled, $"Your eShopOnWeb order #{order.Id} has been cancelled.", null, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithNotificationsSpec(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpec(buyerId), cancellationToken);
        await RefreshProviderStatesAsync(notifications, cancellationToken);

        return orders.Select(order => new OrderView(
            order.Id,
            order.OrderDate,
            order.Status,
            order.Total(),
            notifications.Where(x => x.OrderId == order.Id)
                .Select(x => new NotificationSummary(x.Id, x.Kind, x.ProviderStatus)).ToList())).ToList();
    }

    public async Task<IReadOnlyList<NotificationView>?> GetOrderNotificationsAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId) return null;

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpec(orderId), cancellationToken);
        await RefreshProviderStatesAsync(notifications, cancellationToken);
        return notifications.Select(ToView).ToList();
    }

    public async Task<int?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw new ArgumentException("An idempotency key of at most 128 characters is required.");

        var existingAttempt = await _notifications.FirstOrDefaultAsync(new ResendByKeySpec(notificationId, idempotencyKey), cancellationToken);
        if (existingAttempt is not null) return existingAttempt.Id;

        var original = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (original is null) return null;
        await RefreshProviderStateAsync(original, cancellationToken);
        if (!FailedStatuses.Contains(original.ProviderStatus)) throw new InvalidOperationException("Only a failed or undelivered notification can be resent.");
        if (original.ContentRedacted || original.Body is null) throw new InvalidOperationException("Redacted notification content cannot be resent.");

        var order = await _orders.GetByIdAsync(original.OrderId, cancellationToken);
        if (order?.Status == OrderStatus.Cancelled && original.Kind == NotificationKind.DeliveryFollowUp)
            throw new InvalidOperationException("A delivery follow-up for a cancelled order cannot be resent.");

        var contact = await _contacts.GetByIdAsync(original.ContactNumberId, cancellationToken);
        if (contact is null || !contact.IsActive) throw new InvalidOperationException("The destination is no longer registered.");

        var attempt = new OrderNotification(original.OrderId, original.BuyerId, original.ContactNumberId, original.Kind,
            original.Body, Now, null, original.Id, idempotencyKey);
        attempt = await _notifications.AddAsync(attempt, cancellationToken);
        await SendSafelyAsync(attempt, contact.Value, cancellationToken);
        return attempt.Id;
    }

    public async Task<bool> DisposeContentAsync(int notificationId, CancellationToken cancellationToken)
    {
        var notification = await _notifications.GetByIdAsync(notificationId, cancellationToken);
        if (notification is null) return false;
        if (notification.ContentRedacted) return true;

        if (notification.ProviderMessageSid is not null)
        {
            var state = await _sms.RedactMessageAsync(notification.ProviderMessageSid, cancellationToken);
            notification.RecordProviderState(state);
        }

        notification.Redact();
        await _notifications.UpdateAsync(notification, cancellationToken);
        return true;
    }

    public async Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from > to) throw new ArgumentException("from must be before or equal to to.");
        var provider = await _sms.ListMessagesAsync(from, to, cancellationToken);
        var local = await _notifications.ListAsync(new NotificationsInRangeSpec(from, to), cancellationToken);
        var bySid = local.Where(x => x.ProviderMessageSid is not null).ToDictionary(x => x.ProviderMessageSid!, StringComparer.Ordinal);
        var providerSids = provider.Select(x => x.Sid).ToHashSet(StringComparer.Ordinal);

        var rows = provider.Select(x => new ReconciliationEntry(x.Sid, x.Status, x.DateSent ?? x.DateCreated,
            bySid.TryGetValue(x.Sid, out var match) ? match.Id : null, bySid.ContainsKey(x.Sid))).ToList();
        var missing = local.Where(x => !providerSids.Contains(x.ProviderMessageSid!))
            .Select(x => new MissingProviderEntry(x.Id, x.ProviderMessageSid!, x.ProviderStatus)).ToList();
        return new ReconciliationView(from, to, rows, missing);
    }

    private DateTimeOffset Now => _timeProvider.GetUtcNow();

    private async Task NotifyAllSafelyAsync(Order order, NotificationKind kind, string body, DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        try
        {
            var contacts = await _contacts.ListAsync(new ActiveContactNumbersByBuyerSpec(order.BuyerId), cancellationToken);
            foreach (var contact in contacts)
            {
                try
                {
                    var notification = await _notifications.AddAsync(new OrderNotification(order.Id, order.BuyerId, contact.Id, kind, body, Now, scheduledFor), cancellationToken);
                    await SendSafelyAsync(notification, contact.Value, cancellationToken);
                }
                catch
                {
                    // A provider or notification-store failure never rolls back the order transition.
                }
            }
        }
        catch
        {
            // Contact lookup failure must not fail the order transition either.
        }
    }

    private async Task SendSafelyAsync(OrderNotification notification, string destination, CancellationToken cancellationToken)
    {
        try
        {
            var state = await _sms.SendMessageAsync(destination, notification.Body!, notification.ScheduledFor, cancellationToken);
            notification.RecordProviderState(state);
        }
        catch
        {
            notification.RecordFailure();
        }

        await TryUpdateAsync(notification, cancellationToken);
    }

    private async Task RefreshProviderStatesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications) await RefreshProviderStateAsync(notification, cancellationToken);
    }

    private async Task RefreshProviderStateAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        if (notification.ProviderMessageSid is null) return;
        try
        {
            notification.RecordProviderState(await _sms.GetMessageAsync(notification.ProviderMessageSid, cancellationToken));
            await _notifications.UpdateAsync(notification, cancellationToken);
        }
        catch
        {
            // Return the last persisted provider state when Twilio is temporarily unavailable.
        }
    }

    private async Task<ProviderMessage> CancelWithRetryAsync(string sid, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return await _sms.CancelMessageAsync(sid, cancellationToken); }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < 2) await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }
        throw last!;
    }

    private async Task TryUpdateAsync(OrderNotification notification, CancellationToken cancellationToken)
    {
        try { await _notifications.UpdateAsync(notification, cancellationToken); }
        catch { /* Notification persistence cannot fail an order transition. */ }
    }

    private static bool IsTerminal(string status) =>
        status.Equals("canceled", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("delivered", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("undelivered", StringComparison.OrdinalIgnoreCase);

    private static NotificationView ToView(OrderNotification x) => new(
        x.Id, x.OrderId, x.Kind, x.Body, x.ContentRedacted, x.ProviderMessageSid, x.ProviderStatus,
        x.ProviderErrorCode, x.CreatedAt, x.ScheduledFor, x.ResendOfNotificationId);
}
