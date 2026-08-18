using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationServiceTests
{
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalogRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contactRepo = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<Notification> _notifRepo = Substitute.For<IRepository<Notification>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService() =>
        new(_orderRepo, _catalogRepo, _contactRepo, _notifRepo, _gateway, _uri, _logger);

    private static Order PlacedOrder() =>
        new("buyer@test.com", new Address("1 St", "City", "ST", "Country", "00000"),
            new List<OrderItem> { new(new CatalogItemOrdered(1, "Item", "pic.png"), 9.99m, 1) });

    private void HaveOneContactNumber() =>
        _contactRepo.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new("buyer@test.com", "+15551230000") });

    [Fact]
    public async Task Dispatch_WhenSendFails_StillDispatchesAndRecordsFailure()
    {
        var order = PlacedOrder();
        _orderRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(order);
        HaveOneContactNumber();
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SmsSendResult>(new SmsGatewayException("send failed", 400, 21211)));
        _gateway.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMsched", "scheduled", null, null));
        _notifRepo.AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Notification>());

        var result = await CreateService().DispatchAsync(1, default);

        Assert.Equal(OrderActionResult.Success, result);          // the operation still succeeds
        Assert.Equal(OrderStatus.Dispatched, order.Status);
        await _notifRepo.Received().AddAsync(
            Arg.Is<Notification>(n => n.DeliveryStatus == NotificationDeliveryStatus.SendFailed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispatch_WithNoNumberOnFile_SendsNothing()
    {
        var order = PlacedOrder();
        _orderRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(order);
        _contactRepo.ListAsync(Arg.Any<ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

        var result = await CreateService().DispatchAsync(1, default);

        Assert.Equal(OrderActionResult.Success, result);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_CallsOffScheduledFollowUpBeforeItSends()
    {
        var order = PlacedOrder();
        order.MarkDispatched();
        _orderRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(order);
        HaveOneContactNumber();

        var followUp = new Notification("buyer@test.com", 1, NotificationKind.DeliveryFollowUp, "+15551230000", "how did it go?", isScheduled: true);
        followUp.RecordAccepted("SMsched", "scheduled", null, null);
        _notifRepo.ListAsync(Arg.Any<ISpecification<Notification>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Notification> { followUp });
        _gateway.CancelScheduledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMsched", "canceled", null, null));
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMcancel", "queued", null, null));
        _notifRepo.AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Notification>());

        var result = await CreateService().CancelAsync(1, default);

        Assert.Equal(OrderActionResult.Success, result);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        await _gateway.Received(1).CancelScheduledAsync("SMsched", Arg.Any<CancellationToken>());
        Assert.Equal(NotificationDeliveryStatus.Canceled, followUp.DeliveryStatus);
    }

    [Fact]
    public async Task Resend_UnderSameIdempotencyKey_DoesNotSendAgain()
    {
        var existing = new Notification("buyer@test.com", 1, NotificationKind.OrderPlaced, "+15551230000", "placed", idempotencyKey: "key-1");
        _notifRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<Notification>>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await CreateService().ResendAsync(5, "key-1", default);

        Assert.Equal(ResendOutcome.Duplicate, result.Outcome);
        Assert.Same(existing, result.Notification);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_UnderFreshKey_SendsANewMessage()
    {
        _notifRepo.FirstOrDefaultAsync(Arg.Any<ISpecification<Notification>>(), Arg.Any<CancellationToken>())
            .Returns((Notification?)null);
        var original = new Notification("buyer@test.com", 1, NotificationKind.OrderPlaced, "+15551230000", "placed");
        _notifRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(original);
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMresent", "queued", null, null));
        _notifRepo.AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Notification>());

        var result = await CreateService().ResendAsync(5, "key-2", default);

        Assert.Equal(ResendOutcome.Sent, result.Outcome);
        await _gateway.Received(1).SendAsync("+15551230000", Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal("key-2", result.Notification!.IdempotencyKey);
    }
}
