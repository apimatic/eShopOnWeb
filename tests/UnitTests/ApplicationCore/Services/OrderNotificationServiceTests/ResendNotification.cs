using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

public class ResendNotification
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalogItems = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contactNumbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ITwilioMessagingGateway _gateway = Substitute.For<ITwilioMessagingGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService() =>
        new(_orders, _catalogItems, _contactNumbers, _notifications, _gateway, _uriComposer, _logger);

    [Fact]
    public async Task RepeatUnderSameKeyDoesNotSendAgain()
    {
        var alreadySent = new OrderNotification(2, "buyer", NotificationKind.OrderPlaced, "+15005550006", "hi");
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(alreadySent);

        var result = await CreateService().ResendNotificationAsync(5, "same-key");

        Assert.Same(alreadySent, result);
        await _gateway.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FreshKeySendsAndRecordsNewNotification()
    {
        var original = new OrderNotification(2, "buyer", NotificationKind.OrderPlaced, "+15005550006", "hi");
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(original);
        _gateway.SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderSendResult("SM123", "queued", null, null));

        var result = await CreateService().ResendNotificationAsync(5, "fresh-key");

        await _gateway.Received(1).SendMessageAsync(original.ToNumber, original.Body!, Arg.Any<CancellationToken>());
        await _notifications.Received(1).AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
        Assert.Equal("SM123", result.ProviderMessageSid);
        Assert.Equal("fresh-key", result.IdempotencyKey);
    }

    [Fact]
    public async Task DisposedContentCannotBeResent()
    {
        var disposed = new OrderNotification(2, "buyer", NotificationKind.OrderPlaced, "+15005550006", "hi");
        disposed.MarkContentDisposed();
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(disposed);

        await Assert.ThrowsAsync<NotificationConflictException>(() => CreateService().ResendNotificationAsync(5, "k"));
        await _gateway.DidNotReceive().SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
