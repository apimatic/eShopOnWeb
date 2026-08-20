using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

public class NotifyOrderPlaced
{
    [Fact]
    public async Task DoesNotThrowWhenProviderFails()
    {
        var notifications = Substitute.For<IRepository<OrderNotification>>();
        var contacts = Substitute.For<IRepository<ShopperContactNumber>>();
        var gateway = Substitute.For<IOrderMessagingGateway>();
        var logger = Substitute.For<IAppLogger<OrderNotificationService>>();

        var contact = new ShopperContactNumber("buyer@example.com", "+15555550100", "mobile");
        typeof(ShopperContactNumber).GetProperty("Id")!.SetValue(contact, 1);
        contacts.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), default)
            .Returns(new List<ShopperContactNumber> { contact });
        gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.DateTimeOffset?>(), default)
            .Throws(new OrderMessagingException("rejected", 400));
        notifications.AddAsync(Arg.Any<OrderNotification>(), default)
            .Returns(ci => ci.Arg<OrderNotification>());

        var service = new OrderNotificationService(notifications, contacts, gateway, logger);
        var order = new OrderBuilder().WithDefaultValues();
        typeof(Order).GetProperty("Id")!.SetValue(order, 7);

        await service.NotifyOrderPlacedAsync(order, default);

        await notifications.Received().AddAsync(Arg.Is<OrderNotification>(n => n.Status == "failed"), default);
    }

    [Fact]
    public async Task SkipsSendWhenNoNumberOnFile()
    {
        var notifications = Substitute.For<IRepository<OrderNotification>>();
        var contacts = Substitute.For<IRepository<ShopperContactNumber>>();
        var gateway = Substitute.For<IOrderMessagingGateway>();
        var logger = Substitute.For<IAppLogger<OrderNotificationService>>();
        contacts.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), default)
            .Returns(new List<ShopperContactNumber>());

        var service = new OrderNotificationService(notifications, contacts, gateway, logger);
        await service.NotifyOrderPlacedAsync(new OrderBuilder().WithDefaultValues(), default);

        await gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.DateTimeOffset?>(), default);
    }
}
