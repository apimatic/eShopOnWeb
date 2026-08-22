using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

public class OrderNotificationServiceTests
{
    private readonly IRepository<ShopperContactNumber> _contacts = Substitute.For<IRepository<ShopperContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly IRepository<NotificationResendAttempt> _resends = Substitute.For<IRepository<NotificationResendAttempt>>();
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly ISmsNotificationGateway _gateway = Substitute.For<ISmsNotificationGateway>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService()
        => new(_contacts, _notifications, _resends, _orders, _gateway, _logger);

    [Fact]
    public async Task SkipsSendWhenNoNumberOnFile()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _contacts.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), default)
            .Returns(new List<ShopperContactNumber>());

        await CreateService().NotifyOrderPlacedAsync(order, default);

        await _gateway.DidNotReceiveWithAnyArgs().SendImmediateAsync(default!, default!, default);
        await _notifications.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task StillSucceedsWhenGatewayThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        typeof(Microsoft.eShopWeb.ApplicationCore.Entities.BaseEntity).GetProperty("Id")!.SetValue(order, 7);
        _contacts.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), default)
            .Returns(new List<ShopperContactNumber> { new("12345", "+15555550100") });
        _gateway.SendImmediateAsync(Arg.Any<string>(), Arg.Any<string>(), default)
            .Returns<SmsDispatchResult>(_ => throw new Microsoft.eShopWeb.ApplicationCore.Exceptions.MessagingProviderException("down"));

        await CreateService().NotifyOrderPlacedAsync(order, default);

        await _notifications.Received().AddAsync(Arg.Any<OrderNotification>(), default);
    }

    [Fact]
    public async Task ResendReusesIdempotencyKey()
    {
        var existing = new OrderNotification(1, "12345", OrderNotificationKind.Resend, "+15555550100", "hi", "SMabc", "queued");
        _resends.FirstOrDefaultAsync(Arg.Any<NotificationResendAttemptSpecification>(), default)
            .Returns(new NotificationResendAttempt(3, "key-1", 9));
        _notifications.GetByIdAsync(9, default).Returns(existing);

        var result = await CreateService().ResendAsync(3, "key-1", default);

        Assert.Equal("SMabc", result.ProviderSid);
        await _gateway.DidNotReceiveWithAnyArgs().SendImmediateAsync(default!, default!, default);
    }
}
