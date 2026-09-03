using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class ShopperOrderServiceResendTests
{
    [Fact]
    public async Task ResendWithSameIdempotencyKeyDoesNotSendAgain()
    {
        var existing = new OrderNotification(
            orderId: 1,
            buyerId: "demouser@microsoft.com",
            kind: OrderNotificationKind.Resend,
            destination: "+15551234567",
            body: "resent",
            resendOfNotificationId: 9,
            idempotencyKey: "key-1");
        existing.ApplyProviderAcceptance("SM123", "queued", null, null);

        var notifications = Substitute.For<IRepository<OrderNotification>>();
        notifications.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var sms = Substitute.For<ISmsGateway>();
        var service = new ShopperOrderService(
            Substitute.For<IRepository<Order>>(),
            Substitute.For<IRepository<CatalogItem>>(),
            Substitute.For<IRepository<ShopperContactNumber>>(),
            notifications,
            Substitute.For<IUriComposer>(),
            sms,
            Substitute.For<IAppLogger<ShopperOrderService>>());

        var result = await service.ResendAsync(9, "key-1", CancellationToken.None);

        Assert.Equal("SM123", result.ProviderSid);
        await sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
