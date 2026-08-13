using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.NotificationTests;

public class OrderNotificationServiceTests
{
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _itemRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contactRepo = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<SmsNotification> _notificationRepo = Substitute.For<IRepository<SmsNotification>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService() =>
        new(_orderRepo, _itemRepo, _contactRepo, _notificationRepo, _uriComposer, _gateway, _logger);

    private static CatalogItem CatalogItemWithId(int id)
    {
        var item = new CatalogItem(1, 1, "desc", "Widget", 9.99m, "pic.png");
        // Id is assigned by EF in production; set it here so the catalog lookup matches the requested id.
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(item, id);
        return item;
    }

    private void SetupCatalogAndOrder()
    {
        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("pic.png");
        _itemRepo.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { CatalogItemWithId(1) });
        _orderRepo.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Order>());
        _notificationRepo.AddAsync(Arg.Any<SmsNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<SmsNotification>());
    }

    [Fact]
    public async Task PlacingOrderSucceedsEvenWhenTheMessageCannotBeSent()
    {
        SetupCatalogAndOrder();
        _contactRepo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new("buyer", "+15551234567") });
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SentSmsMessage>(_ => throw new SmsGatewayException("provider down"));
        var service = CreateService();

        // Should not throw despite the send failing.
        await service.PlaceOrderAsync("buyer", new[] { new OrderLineRequest(1, 2) }, null);

        // The failed message is still recorded, marked as failed to send.
        await _notificationRepo.Received().AddAsync(
            Arg.Is<SmsNotification>(n => n.Status == SmsDeliveryStatus.SendFailed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShopperWithNoNumberOnFileIsNotMessaged()
    {
        SetupCatalogAndOrder();
        _contactRepo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());
        var service = CreateService();

        await service.PlaceOrderAsync("buyer", new[] { new OrderLineRequest(1, 1) }, null);

        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notificationRepo.DidNotReceive().AddAsync(Arg.Any<SmsNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellingAnOrderCallsOffTheScheduledFollowUp()
    {
        var order = new Order("buyer", new Address("s", "c", "st", "co", "z"), new List<OrderItem>());
        _orderRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(order);
        _contactRepo.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>()); // avoid extra sends in this test

        var followUp = new SmsNotification("buyer", 0, NotificationType.DeliveryFeedbackRequest, "+15551234567", "hi", isScheduled: true);
        followUp.RecordProviderAccepted("SMscheduled", SmsDeliveryStatus.Scheduled, null, null);
        _notificationRepo.ListAsync(Arg.Any<SmsNotificationsByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<SmsNotification> { followUp });
        var service = CreateService();

        var cancelled = await service.CancelAsync(1);

        Assert.True(cancelled);
        await _gateway.Received().CancelScheduledAsync("SMscheduled", Arg.Any<CancellationToken>());
        Assert.Equal(SmsDeliveryStatus.Canceled, followUp.Status);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }
}
