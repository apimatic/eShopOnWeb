using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class NotificationAdminServiceTests
{
    [Fact]
    public async Task ResendWithSameKeyDoesNotSendTwice()
    {
        var notifications = Substitute.For<IRepository<OrderNotification>>();
        var idempotency = Substitute.For<IRepository<ResendIdempotencyRecord>>();
        var contacts = Substitute.For<IRepository<Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate.ContactNumber>>();
        var sms = Substitute.For<ISmsGateway>();
        var logger = Substitute.For<IAppLogger<NotificationAdminService>>();

        var original = new OrderNotification(1, "buyer", NotificationKind.OrderPlaced, "+15551234567", "hello", "SMabc", "failed", 30003, null, null);
        typeof(OrderNotification).GetProperty("Id")!.SetValue(original, 11);

        var resent = new OrderNotification(1, "buyer", NotificationKind.Resend, "+15551234567", "hello", "SMdef", "queued", null, null, null);
        typeof(OrderNotification).GetProperty("Id")!.SetValue(resent, 22);

        notifications.GetByIdAsync(11, Arg.Any<CancellationToken>()).Returns(original);
        notifications.GetByIdAsync(22, Arg.Any<CancellationToken>()).Returns(resent);
        idempotency.FirstOrDefaultAsync(Arg.Any<ResendIdempotencyByKeySpec>(), Arg.Any<CancellationToken>())
            .Returns((ResendIdempotencyRecord?)null, new ResendIdempotencyRecord("key-1", 11, 22));
        contacts.ListAsync(Arg.Any<ContactNumbersByBuyerSpec>(), Arg.Any<CancellationToken>())
            .Returns(new List<Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate.ContactNumber>
            {
                new("buyer", "+15551234567")
            });
        sms.FetchAsync("SMabc", Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage(true, "SMabc", "failed", "hello", 30003, null, "+15551234567", "+15550001111", DateTimeOffset.UtcNow));
        sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage(true, "SMdef", "queued", "hello", null, null, "+15551234567", "+15550001111", DateTimeOffset.UtcNow));
        notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var n = ci.Arg<OrderNotification>();
                typeof(OrderNotification).GetProperty("Id")!.SetValue(n, 22);
                return n;
            });

        var service = new NotificationAdminService(notifications, idempotency, contacts, sms, logger);

        var first = await service.ResendAsync(11, "key-1", CancellationToken.None);
        var second = await service.ResendAsync(11, "key-1", CancellationToken.None);

        Assert.Equal(22, first.Id);
        Assert.Equal(22, second.Id);
        await sms.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }
}
