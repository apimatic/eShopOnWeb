using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationServiceTests
{
    private const string BuyerId = "shopper@example.com";
    private const string Number = "+14165550123";

    private readonly ISmsNotificationProvider _provider = Substitute.For<ISmsNotificationProvider>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly IReadRepository<ContactNumber> _contactNumbers = Substitute.For<IReadRepository<ContactNumber>>();
    private readonly IReadRepository<Order> _orders = Substitute.For<IReadRepository<Order>>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private readonly List<OrderNotification> _saved = new();

    private OrderNotificationService CreateService()
    {
        // Capture everything AddAsync-ed and hand it an id so downstream code behaves like a real store.
        _notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var n = call.Arg<OrderNotification>();
                _saved.Add(n);
                return n;
            });
        _notifications.ListAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(_ => _saved);
        return new OrderNotificationService(_provider, _notifications, _contactNumbers, _orders, _logger);
    }

    private void RegisterNumber() =>
        _contactNumbers.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new(BuyerId, Number) });

    private void NoNumbers() =>
        _contactNumbers.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

    private static Order NewOrder() => new(BuyerId, new Address("s", "c", "st", "co", "z"),
        new List<OrderItem> { new(new CatalogItemOrdered(1, "item", "pic"), 1m, 1) });

    [Fact]
    public async Task PlacedWithNoNumberOnFileSendsNothing()
    {
        NoNumbers();
        var service = CreateService();

        await service.NotifyOrderPlacedAsync(NewOrderWithId(1));

        await _provider.DidNotReceiveWithAnyArgs().SendAsync(default!, default!);
        await _notifications.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task PlacedWithNumberSendsAndRecordsAcceptedNotification()
    {
        RegisterNumber();
        _provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage { Sid = "SM1", Status = "queued" });
        var service = CreateService();

        await service.NotifyOrderPlacedAsync(NewOrderWithId(1));

        Assert.Single(_saved);
        Assert.Equal("SM1", _saved[0].MessageSid);
        Assert.Equal("queued", _saved[0].DeliveryStatus);
        Assert.Equal(NotificationType.OrderPlaced, _saved[0].Type);
    }

    [Fact]
    public async Task SendFailureNeverThrowsAndRecordsFailedNotification()
    {
        RegisterNumber();
        _provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new NotificationProviderException("provider down"));
        var service = CreateService();

        // Must not throw — the order action must still succeed.
        await service.NotifyOrderPlacedAsync(NewOrderWithId(1));

        Assert.Single(_saved);
        Assert.Equal(OrderNotification.StatusFailed, _saved[0].DeliveryStatus);
        Assert.Null(_saved[0].MessageSid);
    }

    [Fact]
    public async Task DispatchSendsMessageAndSchedulesFollowUp()
    {
        RegisterNumber();
        _provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage { Sid = "SM-dispatch", Status = "queued" });
        _provider.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage { Sid = "SM-followup", Status = "scheduled" });
        var service = CreateService();

        await service.NotifyOrderDispatchedAsync(NewOrderWithId(2));

        await _provider.Received(1).ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.DateTimeOffset>(), Arg.Any<CancellationToken>());
        Assert.Contains(_saved, n => n.Type == NotificationType.OrderDispatched);
        var followUp = Assert.Single(_saved, n => n.IsFollowUp);
        Assert.Equal("SM-followup", followUp.MessageSid);
        Assert.NotNull(followUp.ScheduledSendAt);
    }

    [Fact]
    public async Task CancelCallsOffAPendingFollowUp()
    {
        RegisterNumber();
        _provider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage { Sid = "SM-cancel", Status = "queued" });
        _provider.CancelScheduledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage { Sid = "SM-followup", Status = "canceled" });

        // A scheduled follow-up already exists for the order.
        var followUp = new OrderNotification(3, BuyerId, NotificationType.DeliveryFollowUp, Number, "body", isFollowUp: true,
            scheduledSendAt: System.DateTimeOffset.UtcNow.AddDays(3));
        followUp.MarkAccepted("SM-followup", OrderNotification.StatusScheduled, null);
        _saved.Add(followUp);
        var service = CreateService();

        await service.NotifyOrderCancelledAsync(NewOrderWithId(3));

        await _provider.Received(1).CancelScheduledAsync("SM-followup", Arg.Any<CancellationToken>());
        Assert.Equal(OrderNotification.StatusCanceled, followUp.DeliveryStatus);
    }

    [Fact]
    public async Task ResendUnderSameKeyDoesNotSendASecondMessage()
    {
        var prior = new OrderNotification(4, BuyerId, NotificationType.OrderPlaced, Number, "body");
        prior.MarkAsResendOf(10, "key-1");
        prior.MarkAccepted("SM-prior", "sent", null);
        _notifications.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(prior);
        var service = CreateService();

        var result = await service.ResendAsync(10, "key-1");

        Assert.Same(prior, result);
        await _provider.DidNotReceiveWithAnyArgs().SendAsync(default!, default!);
    }

    [Fact]
    public async Task ResendOfDisposedContentIsRejected()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        var source = new OrderNotification(5, BuyerId, NotificationType.OrderPlaced, Number, "body");
        source.MarkAccepted("SM-x", "undelivered", 30006);
        source.DisposeContent();
        _notifications.GetByIdAsync(11, Arg.Any<CancellationToken>()).Returns(source);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidNotificationOperationException>(() => service.ResendAsync(11, "fresh-key"));
        await _provider.DidNotReceiveWithAnyArgs().SendAsync(default!, default!);
    }

    [Fact]
    public async Task ResendToRemovedNumberIsRejected()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        var source = new OrderNotification(6, BuyerId, NotificationType.OrderPlaced, Number, "body");
        source.MarkAccepted("SM-y", "undelivered", 30006);
        _notifications.GetByIdAsync(12, Arg.Any<CancellationToken>()).Returns(source);
        _contactNumbers.AnyAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>()).Returns(false);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidNotificationOperationException>(() => service.ResendAsync(12, "fresh-key"));
        await _provider.DidNotReceiveWithAnyArgs().SendAsync(default!, default!);
    }

    private static Order NewOrderWithId(int id)
    {
        var order = NewOrder();
        // Order.Id is set by EF in the real store; set it here via the base entity for the test.
        typeof(Microsoft.eShopWeb.ApplicationCore.Entities.BaseEntity)
            .GetProperty(nameof(Microsoft.eShopWeb.ApplicationCore.Entities.BaseEntity.Id))!
            .SetValue(order, id);
        return order;
    }
}
