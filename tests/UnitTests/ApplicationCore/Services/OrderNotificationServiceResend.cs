using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationServiceResend
{
    [Fact]
    public async Task RepeatingTheSameIdempotencyKeyDoesNotSendAgain()
    {
        var notifications = Substitute.For<IRepository<OrderNotification>>();
        var contacts = Substitute.For<IRepository<ShopperContactNumber>>();
        var keys = Substitute.For<IRepository<NotificationResendIdempotency>>();
        var gateway = Substitute.For<ISmsGateway>();
        var logger = Substitute.For<IAppLogger<OrderNotificationService>>();

        var previous = new OrderNotification(1, "buyer", OrderNotificationKind.OrderPlaced, 9, "+10000000000", "hello");
        var record = new NotificationResendIdempotency(4, "key-1", 12);

        notifications.GetByIdAsync(12, Arg.Any<CancellationToken>()).Returns(previous);
        keys.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<NotificationResendIdempotency>>(), Arg.Any<CancellationToken>())
            .Returns(record);

        var service = new OrderNotificationService(notifications, contacts, keys, gateway, logger);

        var result = await service.ResendAsync(4, "key-1", CancellationToken.None);

        Assert.Same(previous, result);
        await gateway.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }
}
