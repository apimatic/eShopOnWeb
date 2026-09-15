using System.Collections.Generic;
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
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationServiceTests
{
    private readonly IRepository<Order> _orders = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalog = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<ContactNumber> _contacts = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ISmsNotificationService _sms = Substitute.For<ISmsNotificationService>();
    private readonly IUriComposer _uri = Substitute.For<IUriComposer>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();
    private readonly OrderNotificationService _service;

    public OrderNotificationServiceTests()
    {
        _service = new OrderNotificationService(_orders, _catalog, _contacts, _notifications,
            _sms, new KeyedResendIdempotencyGuard(), _uri, _logger);

        // Default: sensible provider responses so scheduling/sending don't NRE unless a test overrides.
        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SentMessage("SM-sent", "sent"));
        _sms.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SentMessage("SM-sched", "scheduled"));
        _notifications.ListAsync(Arg.Any<OrderNotificationsByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification>());
    }

    private void OrderExists(int id, Order order) =>
        _orders.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(order);

    private void ContactsOnFile(params ContactNumber[] numbers) =>
        _contacts.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<ContactNumber>(numbers));

    [Fact]
    public async Task Dispatch_WithNoNumberOnFile_DoesNotMessage()
    {
        OrderExists(1, new OrderBuilder().WithDefaultValues());
        ContactsOnFile();

        var order = await _service.DispatchOrderAsync(1);

        Assert.NotNull(order);
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sms.DidNotReceive().ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispatch_WhenSendFails_StillSucceeds_AndRecordsFailure()
    {
        OrderExists(1, new OrderBuilder().WithDefaultValues());
        ContactsOnFile(new ContactNumber("12345", "+15551234567"));
        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new SmsNotificationException("provider rejected"));

        var order = await _service.DispatchOrderAsync(1); // must not throw

        Assert.NotNull(order);
        await _notifications.Received().AddAsync(
            Arg.Is<OrderNotification>(n => n.Kind == NotificationKind.OrderDispatched && n.Status == NotificationStatus.SendFailed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispatch_Happy_RecordsDispatchedMessageAndSchedulesFollowUp()
    {
        OrderExists(1, new OrderBuilder().WithDefaultValues());
        ContactsOnFile(new ContactNumber("12345", "+15551234567"));

        await _service.DispatchOrderAsync(1);

        await _notifications.Received().AddAsync(
            Arg.Is<OrderNotification>(n => n.Kind == NotificationKind.OrderDispatched && n.ProviderMessageSid == "SM-sent"),
            Arg.Any<CancellationToken>());
        await _notifications.Received().AddAsync(
            Arg.Is<OrderNotification>(n => n.Kind == NotificationKind.DeliveryFollowUp && n.Status == "scheduled" && n.ProviderMessageSid == "SM-sched"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_CallsOffAScheduledFollowUp()
    {
        OrderExists(1, new OrderBuilder().WithDefaultValues());
        ContactsOnFile(new ContactNumber("12345", "+15551234567"));

        var followUp = new OrderNotification(1, "12345", NotificationKind.DeliveryFollowUp, "+15551234567", "scheduled");
        followUp.MarkSent("SM-followup", "scheduled");
        _notifications.ListAsync(Arg.Any<OrderNotificationsByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });

        var order = await _service.CancelOrderAsync(1);

        Assert.NotNull(order);
        await _sms.Received(1).CancelScheduledAsync("SM-followup", Arg.Any<CancellationToken>());
        await _sms.Received().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()); // cancellation SMS
    }

    [Fact]
    public async Task Resend_UnderSameKey_ReturnsExistingWithoutSending()
    {
        var existing = new OrderNotification(1, "12345", NotificationKind.OrderPlaced, "+15551234567", "undelivered");
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await _service.ResendAsync(5, "dupe-key");

        Assert.Same(existing, result);
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_MissingSource_ReturnsNull()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns((OrderNotification?)null);

        var result = await _service.ResendAsync(5, "fresh-key");

        Assert.Null(result);
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_UnderFreshKey_SendsAndRecordsWithKey()
    {
        var source = new OrderNotification(9, "12345", NotificationKind.OrderPlaced, "+15551234567", "undelivered");
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(source);
        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SentMessage("SM-resent", "queued"));

        var result = await _service.ResendAsync(5, "fresh-key");

        Assert.NotNull(result);
        Assert.Equal("SM-resent", result!.ProviderMessageSid);
        Assert.Equal("fresh-key", result.IdempotencyKey);
        await _sms.Received(1).SendAsync("+15551234567", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.Received().AddAsync(Arg.Is<OrderNotification>(n => n.IdempotencyKey == "fresh-key"), Arg.Any<CancellationToken>());
    }
}
