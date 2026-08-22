using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

public class ResendIdempotency
{
    [Fact]
    public async Task ReusesResultForSameIdempotencyKey()
    {
        var orders = Substitute.For<IRepository<Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order>>();
        var notifications = Substitute.For<IRepository<OrderNotification>>();
        var contactNumbers = Substitute.For<IContactNumberService>();
        var sms = Substitute.For<ISmsNotificationGateway>();
        var logger = Substitute.For<IAppLogger<OrderNotificationService>>();

        var existing = new OrderNotification(1, "buyer", NotificationKind.OrderPlaced, "body", 1, parentNotificationId: 9, idempotencyKey: "key-1");
        existing.RecordSendResult("SM999", "queued", null, null);

        notifications.FirstOrDefaultAsync(Arg.Any<NotificationByParentAndIdempotencySpec>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var service = new OrderNotificationService(orders, notifications, contactNumbers, sms, logger);
        var result = await service.ResendAsync(9, "key-1", CancellationToken.None);

        Assert.Same(existing, result);
        await sms.DidNotReceiveWithAnyArgs().SendImmediateAsync(default!, default!, default);
    }
}
