using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.Infrastructure.Sms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Sms;

public class OrderNotificationServiceTests
{
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly IReadRepository<ContactNumber> _contactNumbers = Substitute.For<IReadRepository<ContactNumber>>();

    private OrderNotificationService NewService() => new(
        _gateway, _notifications, _contactNumbers,
        Options.Create(new TwilioSettings { FromNumber = "+15005550006" }),
        NullLogger<OrderNotificationService>.Instance);

    private static Order OrderWithId(int id, string buyer)
    {
        var order = new Order(buyer, new Address("s", "c", "st", "co", "00000"), new List<OrderItem>());
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(order, id);
        return order;
    }

    [Fact]
    public async Task NotifyOrderPlaced_WithNoNumberOnFile_SendsNothingAndRecordsNothing()
    {
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>());

        await NewService().NotifyOrderPlacedAsync(OrderWithId(10, "buyer@x.com"));

        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyOrderPlaced_WhenProviderFails_DoesNotThrowAndRecordsNotSent()
    {
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber> { new("buyer@x.com", "+15145551588") });

        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SmsGatewayException("down", SmsGatewayErrorKind.Transient));

        OrderNotification? recorded = null;
        _notifications.AddAsync(Arg.Do<OrderNotification>(n => recorded = n), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<OrderNotification>());

        // The order operation must still succeed even though the message could not be sent.
        await NewService().NotifyOrderPlacedAsync(OrderWithId(10, "buyer@x.com"));

        Assert.NotNull(recorded);
        Assert.Equal(OrderNotification.NotSentStatus, recorded!.Status);
        Assert.Null(recorded.ProviderMessageSid);
    }

    [Fact]
    public async Task Resend_UnderSameIdempotencyKey_ReturnsFirstResultWithoutSendingAgain()
    {
        var alreadySent = new OrderNotification(5, "buyer@x.com", "+15145551588", NotificationKind.Resend, "body");
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(alreadySent);

        var result = await NewService().ResendAsync(5, "same-key");

        Assert.Equal(ResendStatus.Sent, result.Status);
        Assert.Same(alreadySent, result.Notification);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_WhenDestinationNoLongerOnFile_DoesNotSend()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(7, Arg.Any<CancellationToken>())
            .Returns(new OrderNotification(5, "buyer@x.com", "+15145551588", NotificationKind.OrderPlaced, "body"));
        _contactNumbers.FirstOrDefaultAsync(Arg.Any<ContactNumberByOwnerAndValueSpecification>(), Arg.Any<CancellationToken>())
            .Returns((ContactNumber?)null);

        var result = await NewService().ResendAsync(7, "fresh-key");

        Assert.Equal(ResendStatus.NumberNoLongerOnFile, result.Status);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
