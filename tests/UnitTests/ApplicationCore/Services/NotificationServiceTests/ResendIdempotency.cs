using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.NotificationServiceTests;

public class ResendIdempotency
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalogItems = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contactNumbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ISmsProvider _sms = Substitute.For<ISmsProvider>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService() =>
        new(_orders, _catalogItems, _contactNumbers, _notifications, _sms, _uri, _logger);

    private static OrderNotification SentOriginal()
    {
        var n = new OrderNotification(5, "buyer@test", NotificationType.OrderPlaced, "+15005550006", "your order was placed");
        n.MarkSent("SMoriginal", MessageDeliveryStatus.Undelivered);
        return n;
    }

    [Fact]
    public async Task RepeatUnderSameKeyDoesNotSendAgain()
    {
        var original = SentOriginal();
        var existingResend = new OrderNotification(5, "buyer@test", NotificationType.OrderPlaced, "+15005550006", "your order was placed");
        existingResend.SetIdempotencyKey("key-1");

        _notifications.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(original);
        _notifications.FirstOrDefaultAsync(Arg.Any<ResendByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(existingResend);

        var result = await CreateService().ResendAsync(1, "key-1");

        Assert.True(result.Found);
        Assert.False(result.ContentDisposed);
        Assert.Same(existingResend, result.Notification);
        // No second message may be sent under a repeated key.
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FreshKeyProducesANewSend()
    {
        var original = SentOriginal();
        _notifications.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(original);
        _notifications.FirstOrDefaultAsync(Arg.Any<ResendByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<OrderNotification>());
        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage("SMnew", MessageDeliveryStatus.Queued, null, null, null, null, null, null));

        var result = await CreateService().ResendAsync(1, "key-2");

        Assert.True(result.Found);
        Assert.False(result.ContentDisposed);
        Assert.NotNull(result.Notification);
        Assert.Equal("SMnew", result.Notification!.ProviderMessageSid);
        Assert.Equal("key-2", result.Notification.IdempotencyKey);
        await _sms.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendOfDisposedContentIsRefusedAndSendsNothing()
    {
        var original = SentOriginal();
        original.MarkContentRedacted(); // body disposed of
        _notifications.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(original);
        _notifications.FirstOrDefaultAsync(Arg.Any<ResendByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);

        var result = await CreateService().ResendAsync(1, "key-3");

        Assert.True(result.Found);
        Assert.True(result.ContentDisposed);
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownNotificationIsNotFound()
    {
        _notifications.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((OrderNotification?)null);

        var result = await CreateService().ResendAsync(99, "key-x");

        Assert.False(result.Found);
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
