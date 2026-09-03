using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Messaging;

public sealed class OrderNotificationServiceTests : IDisposable
{
    private const string BuyerA = "buyer-a";
    private const string BuyerB = "buyer-b";
    private const string CanonicalNumberA = "+15550000001";
    private const string CanonicalNumberB = "+15550000002";

    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly ShippingAddressInput ShippingAddress =
        new("1 Main Street", "Toronto", "ON", "Canada", "M5V 1E6");

    private readonly CatalogContext _context;
    private readonly FakeTwilioMessagingGateway _gateway = new();
    private readonly OrderNotificationService _service;

    public OrderNotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"order-notifications-{Guid.NewGuid():N}")
            .Options;
        _context = new CatalogContext(options);
        _service = new OrderNotificationService(
            _context,
            _gateway,
            new FixedTimeProvider(Now),
            NullLogger<OrderNotificationService>.Instance);
    }

    [Fact]
    public async Task RegistrationRejectsInvalidDestinationAndStoresProviderCanonicalNumber()
    {
        _gateway.Validation = _ => new PhoneValidationResult(false, null);

        await Assert.ThrowsAsync<InvalidDestinationException>(() =>
            _service.RegisterContactNumberAsync(BuyerA, "not-a-number", default));
        Assert.Empty(await _context.ContactNumbers.ToListAsync());

        _gateway.Validation = _ => new PhoneValidationResult(true, CanonicalNumberA);
        var id = await _service.RegisterContactNumberAsync(BuyerA, "(555) 000-0001", default);

        var stored = await _context.ContactNumbers.SingleAsync();
        Assert.Equal(id, stored.Id);
        Assert.Equal(CanonicalNumberA, stored.PhoneNumber);
    }

    [Fact]
    public async Task ContactNumbersAndOrdersAreBuyerIsolated()
    {
        var contactA = await RegisterAsync(BuyerA, CanonicalNumberA);
        await RegisterAsync(BuyerB, CanonicalNumberB);
        var orderA = await PlaceOrderAsync(BuyerA);

        var buyerBNumbers = await _service.GetContactNumbersAsync(BuyerB, default);

        Assert.Single(buyerBNumbers);
        Assert.Equal(BuyerB, buyerBNumbers[0].BuyerId);
        Assert.False(await _service.DeleteContactNumberAsync(BuyerB, contactA, default));
        Assert.NotNull(await _context.ContactNumbers.SingleAsync(number => number.Id == contactA && number.DeletedAt == null));
        Assert.Null(await _service.GetOrderNotificationsAsync(BuyerB, orderA, default));
        Assert.DoesNotContain(await _service.GetOrdersAsync(BuyerB, default), order => order.Id == orderA);
    }

    [Fact]
    public async Task OrderWithoutContactNumberSucceedsWithoutSending()
    {
        var orderId = await PlaceOrderAsync(BuyerA);

        Assert.True(await _context.Orders.AnyAsync(order => order.Id == orderId));
        Assert.Empty(_gateway.SendCalls);
        Assert.Empty(await _context.OrderNotifications.ToListAsync());
    }

    [Fact]
    public async Task ProviderSendFailureDoesNotFailOrderAndIsRecorded()
    {
        await RegisterAsync(BuyerA, CanonicalNumberA);
        _gateway.Send = _ => throw new TwilioProviderException(
            "provider rejected send",
            HttpStatusCode.BadRequest,
            new InvalidOperationException());

        var orderId = await PlaceOrderAsync(BuyerA);

        Assert.True(await _context.Orders.AnyAsync(order => order.Id == orderId));
        var notification = await _context.OrderNotifications.SingleAsync();
        Assert.Equal(OrderNotificationStatus.Failed, notification.Status);
        Assert.Equal((int)HttpStatusCode.BadRequest, notification.ProviderErrorCode);
        Assert.Single(_gateway.SendCalls);
    }

    [Fact]
    public async Task DispatchSendsImmediateMessageAndSchedulesFollowUpForThreeDaysLater()
    {
        await RegisterAsync(BuyerA, CanonicalNumberA);
        var orderId = await PlaceOrderAsync(BuyerA);
        _gateway.SendCalls.Clear();

        var result = await _service.DispatchOrderAsync(orderId, default);

        Assert.Equal(OrderTransitionResult.Success, result);
        Assert.Equal(2, _gateway.SendCalls.Count);
        Assert.All(_gateway.SendCalls, call => Assert.Equal(CanonicalNumberA, call.Destination));
        Assert.Null(_gateway.SendCalls[0].SendAt);
        Assert.Contains("dispatched", _gateway.SendCalls[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Now.AddDays(3), _gateway.SendCalls[1].SendAt);
        Assert.Contains("How did delivery", _gateway.SendCalls[1].Body, StringComparison.Ordinal);

        var notifications = await _context.OrderNotifications
            .Where(notification => notification.OrderId == orderId)
            .ToListAsync();
        Assert.Contains(notifications, notification =>
            notification.Kind == OrderNotificationKind.OrderDispatched && notification.ScheduledFor == null);
        Assert.Contains(notifications, notification =>
            notification.Kind == OrderNotificationKind.DeliveryFollowUp && notification.ScheduledFor == Now.AddDays(3));
    }

    [Fact]
    public async Task CancelCancelsScheduledFollowUpAndSendsCancellationNotice()
    {
        await RegisterAsync(BuyerA, CanonicalNumberA);
        var orderId = await PlaceOrderAsync(BuyerA);
        await _service.DispatchOrderAsync(orderId, default);
        var scheduled = await _context.OrderNotifications.SingleAsync(notification =>
            notification.OrderId == orderId && notification.Kind == OrderNotificationKind.DeliveryFollowUp);
        _gateway.SendCalls.Clear();

        var result = await _service.CancelOrderAsync(orderId, default);

        Assert.Equal(OrderTransitionResult.Success, result);
        Assert.Equal(new[] { scheduled.ProviderMessageSid }, _gateway.CancelledSids);
        var cancellation = Assert.Single(_gateway.SendCalls);
        Assert.Null(cancellation.SendAt);
        Assert.Contains("cancelled", cancellation.Body, StringComparison.OrdinalIgnoreCase);

        await _context.Entry(scheduled).ReloadAsync();
        Assert.Equal(OrderNotificationStatus.Canceled, scheduled.Status);
        Assert.NotNull(scheduled.CancellationCompletedAt);
    }

    [Fact]
    public async Task DeletingContactCancelsScheduledMessageAndPreventsFutureSends()
    {
        var contactId = await RegisterAsync(BuyerA, CanonicalNumberA);
        var orderId = await PlaceOrderAsync(BuyerA);
        await _service.DispatchOrderAsync(orderId, default);
        var scheduled = await _context.OrderNotifications.SingleAsync(notification =>
            notification.OrderId == orderId && notification.Kind == OrderNotificationKind.DeliveryFollowUp);
        _gateway.SendCalls.Clear();

        Assert.True(await _service.DeleteContactNumberAsync(BuyerA, contactId, default));
        await PlaceOrderAsync(BuyerA);

        Assert.Equal(new[] { scheduled.ProviderMessageSid }, _gateway.CancelledSids);
        Assert.Empty(_gateway.SendCalls);
        Assert.Empty(await _service.GetContactNumbersAsync(BuyerA, default));
    }

    [Fact]
    public async Task ResendIsIdempotentPerKeyAndFreshKeyCreatesAnotherAttempt()
    {
        await RegisterAsync(BuyerA, CanonicalNumberA);
        _gateway.Send = call => _gateway.MessageFor(call, status: "undelivered");
        var orderId = await PlaceOrderAsync(BuyerA);
        var source = await _context.OrderNotifications.SingleAsync(notification => notification.OrderId == orderId);
        _gateway.Send = call => _gateway.MessageFor(call);
        _gateway.SendCalls.Clear();

        var first = await _service.ResendAsync(source.Id, "attempt-1", default);
        var replay = await _service.ResendAsync(source.Id, "attempt-1", default);
        var second = await _service.ResendAsync(source.Id, "attempt-2", default);

        Assert.NotNull(first);
        Assert.Equal(first, replay);
        Assert.NotEqual(first, second);
        Assert.Equal(2, _gateway.SendCalls.Count);
        Assert.Equal(2, await _context.OrderNotifications.CountAsync(notification => notification.SourceNotificationId == source.Id));
    }

    [Fact]
    public async Task ContentIsClearedLocallyOnlyAfterProviderConfirmsDisposal()
    {
        await RegisterAsync(BuyerA, CanonicalNumberA);
        var orderId = await PlaceOrderAsync(BuyerA);
        var notification = await _context.OrderNotifications.SingleAsync(notification => notification.OrderId == orderId);
        _gateway.Dispose = _ => throw new TwilioProviderException(
            "provider did not confirm disposal",
            HttpStatusCode.ServiceUnavailable,
            new InvalidOperationException());

        await Assert.ThrowsAsync<TwilioProviderException>(() =>
            _service.DisposeNotificationContentAsync(notification.Id, default));
        Assert.NotNull(notification.Body);
        Assert.Null(notification.ContentDisposedAt);

        _gateway.Dispose = sid => _gateway.ProviderMessage(sid, body: null);
        var result = await _service.DisposeNotificationContentAsync(notification.Id, default);

        Assert.Equal(ContentDisposalResult.Success, result);
        Assert.Equal(new[] { notification.ProviderMessageSid, notification.ProviderMessageSid }, _gateway.DisposedSids);
        Assert.Null(notification.Body);
        Assert.NotNull(notification.ContentDisposedAt);
    }

    [Fact]
    public async Task ReconciliationReportsMatchedProviderOnlyAndLocalOnlyMessages()
    {
        await RegisterAsync(BuyerA, CanonicalNumberA);
        var matchedOrder = await PlaceOrderAsync(BuyerA);
        var matched = await _context.OrderNotifications.SingleAsync(notification => notification.OrderId == matchedOrder);

        _gateway.Send = _ => throw new TwilioProviderException(
            "provider rejected send",
            HttpStatusCode.BadRequest,
            new InvalidOperationException());
        var localOnlyOrder = await PlaceOrderAsync(BuyerA);
        var localOnly = await _context.OrderNotifications.SingleAsync(notification => notification.OrderId == localOnlyOrder);
        var providerOnly = _gateway.ProviderMessage("SM-provider-only", dateCreated: Now);
        _gateway.ListMessages = new[]
        {
            _gateway.ProviderMessage(matched.ProviderMessageSid!, dateCreated: Now),
            providerOnly
        };

        var rows = await _service.ReconcileAsync(Now.AddMinutes(-1), Now.AddMinutes(1), default);

        Assert.Contains(rows, row => row.Match == ReconciliationMatch.Matched && row.NotificationId == matched.Id);
        Assert.Contains(rows, row => row.Match == ReconciliationMatch.ProviderOnly && row.ProviderMessageSid == providerOnly.Sid);
        Assert.Contains(rows, row => row.Match == ReconciliationMatch.LocalOnly && row.NotificationId == localOnly.Id);
        Assert.Equal((Now.AddMinutes(-1), Now.AddMinutes(1)), _gateway.LastListRange);
    }

    public void Dispose() => _context.Dispose();

    private async Task<int> RegisterAsync(string buyerId, string canonicalNumber)
    {
        _gateway.Validation = _ => new PhoneValidationResult(true, canonicalNumber);
        return await _service.RegisterContactNumberAsync(buyerId, canonicalNumber, default);
    }

    private async Task<int> PlaceOrderAsync(string buyerId)
    {
        var item = new CatalogItem(1, 1, "Description", $"Item-{Guid.NewGuid():N}", 12.50m, "image.png");
        _context.CatalogItems.Add(item);
        await _context.SaveChangesAsync();
        return await _service.PlaceOrderAsync(
            buyerId,
            new[] { new OrderLineInput(item.Id, 2) },
            ShippingAddress,
            default);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeTwilioMessagingGateway : ITwilioMessagingGateway
    {
        private int _sequence;

        public Func<string, PhoneValidationResult> Validation { get; set; } =
            input => new PhoneValidationResult(true, input);
        public Func<SendCall, ProviderMessage> Send { get; set; }
        public Func<string, ProviderMessage> Dispose { get; set; }
        public List<SendCall> SendCalls { get; } = new();
        public List<string> CancelledSids { get; } = new();
        public List<string> DisposedSids { get; } = new();
        public IReadOnlyList<ProviderMessage> ListMessages { get; set; } = Array.Empty<ProviderMessage>();
        public (DateTimeOffset From, DateTimeOffset To)? LastListRange { get; private set; }

        public FakeTwilioMessagingGateway()
        {
            Send = call => MessageFor(call);
            Dispose = sid => ProviderMessage(sid, body: null);
        }

        public Task<PhoneValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken) =>
            Task.FromResult(Validation(phoneNumber));

        public Task<ProviderMessage> SendAsync(
            string destination,
            string body,
            DateTimeOffset? sendAt,
            CancellationToken cancellationToken)
        {
            var call = new SendCall(destination, body, sendAt);
            SendCalls.Add(call);
            return Task.FromResult(Send(call));
        }

        public Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderMessage(providerMessageSid));

        public Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            CancelledSids.Add(providerMessageSid);
            return Task.FromResult(ProviderMessage(providerMessageSid, "canceled"));
        }

        public Task<ProviderMessage> DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken)
        {
            DisposedSids.Add(providerMessageSid);
            return Task.FromResult(Dispose(providerMessageSid));
        }

        public Task<IReadOnlyList<ProviderMessage>> ListAsync(
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken)
        {
            LastListRange = (from, to);
            return Task.FromResult(ListMessages);
        }

        public ProviderMessage MessageFor(SendCall call, string status = "queued") =>
            ProviderMessage($"SM{++_sequence:D6}", status, call.Body, Now);

        public ProviderMessage ProviderMessage(
            string sid,
            string status = "queued",
            string? body = "message",
            DateTimeOffset? dateCreated = null) =>
            new(
                sid,
                status,
                null,
                null,
                "+15559999999",
                CanonicalNumberA,
                body,
                "MG000001",
                dateCreated ?? Now,
                null,
                dateCreated ?? Now);
    }

    private sealed record SendCall(string Destination, string Body, DateTimeOffset? SendAt);
}
