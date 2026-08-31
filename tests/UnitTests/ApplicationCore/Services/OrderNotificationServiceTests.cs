using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class OrderNotificationServiceTests
{
    private readonly IRepository<ContactNumber> _contactNumbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ISmsProvider _smsProvider = Substitute.For<ISmsProvider>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService() =>
        new(_contactNumbers, _notifications, _smsProvider, _logger);

    private static Order CreateOrder(int id = 1)
    {
        var order = new Order("demouser@microsoft.com",
            new Address("1 Main St", "Seattle", "WA", "US", "98101"),
            new List<OrderItem>
            {
                new(new CatalogItemOrdered(1, "Test mug", "mug.png"), 10m, 2)
            });
        return order;
    }

    private void GivenNumbers(params ContactNumber[] numbers)
    {
        _contactNumbers.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<ContactNumber>>(), Arg.Any<CancellationToken>())
            .Returns(numbers.ToList());
    }

    [Fact]
    public async Task NotifyOrderPlaced_NoNumberOnFile_SendsNothing()
    {
        GivenNumbers();
        var service = CreateService();

        await service.NotifyOrderPlacedAsync(CreateOrder());

        await _smsProvider.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyOrderPlaced_ProviderFailure_RecordsSendFailedAndDoesNotThrow()
    {
        var number = new ContactNumber("demouser@microsoft.com", "+14155552671");
        GivenNumbers(number);
        _smsProvider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new SmsProviderException("rejected", System.Net.HttpStatusCode.BadRequest));
        OrderNotification? recorded = null;
        _notifications.AddAsync(Arg.Do<OrderNotification>(n => recorded = n), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<OrderNotification>());

        var service = CreateService();
        await service.NotifyOrderPlacedAsync(CreateOrder());

        Assert.NotNull(recorded);
        Assert.Equal(OrderNotification.SendFailedStatus, recorded!.Status);
        Assert.Null(recorded.ProviderMessageSid);
    }

    [Fact]
    public async Task NotifyOrderDispatched_SendsNowAndSchedulesFollowUp()
    {
        var number = new ContactNumber("demouser@microsoft.com", "+14155552671");
        GivenNumbers(number);
        _smsProvider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMsend", "queued"));
        _smsProvider.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMsched", "scheduled"));

        var service = CreateService();
        await service.NotifyOrderDispatchedAsync(CreateOrder(), TimeSpan.FromDays(3));

        await _smsProvider.Received(1).SendAsync("+14155552671", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _smsProvider.Received(1).ScheduleAsync("+14155552671", Arg.Any<string>(),
            Arg.Is<DateTimeOffset>(d => d > DateTimeOffset.UtcNow.AddDays(2)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyOrderCancelled_CancelsScheduledFollowUps()
    {
        GivenNumbers();
        var followUp = new OrderNotification(1, "demouser@microsoft.com", 5, "+14155552671",
            NotificationKind.DeliveryFollowUp, "how was it?", DateTimeOffset.UtcNow.AddDays(3));
        followUp.MarkAccepted("SMsched", "scheduled");
        _notifications.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { followUp });

        var service = CreateService();
        await service.NotifyOrderCancelledAsync(CreateOrder());

        await _smsProvider.Received(1).CancelScheduledAsync("SMsched", Arg.Any<CancellationToken>());
        Assert.Equal(NotificationStatus.Canceled, followUp.Status);
    }

    [Fact]
    public async Task Resend_SameIdempotencyKey_DoesNotSendTwice()
    {
        var existing = new OrderNotification(1, "demouser@microsoft.com", 5, "+14155552671",
            NotificationKind.OrderPlaced, "placed", idempotencyKey: "key-1");
        _notifications.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var service = CreateService();
        var result = await service.ResendAsync(1, "key-1");

        Assert.Equal(ResendOutcome.DuplicateIdempotencyKey, result.Outcome);
        Assert.Same(existing, result.Notification);
        await _smsProvider.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_RemovedContactNumber_IsRefused()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        var original = new OrderNotification(1, "demouser@microsoft.com", 5, "+14155552671",
            NotificationKind.OrderPlaced, "placed");
        original.DetachContactNumber();
        _notifications.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(original);

        var service = CreateService();
        var result = await service.ResendAsync(1, "fresh-key");

        Assert.Equal(ResendOutcome.ContactNumberRemoved, result.Outcome);
        await _smsProvider.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_FreshKey_SendsAndRecordsNewNotification()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<OrderNotification>>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        var original = new OrderNotification(1, "demouser@microsoft.com", 5, "+14155552671",
            NotificationKind.OrderPlaced, "placed");
        _notifications.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(original);
        _contactNumbers.GetByIdAsync(5, Arg.Any<CancellationToken>())
            .Returns(new ContactNumber("demouser@microsoft.com", "+14155552671"));
        _smsProvider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMresend", "queued"));
        OrderNotification? recorded = null;
        _notifications.AddAsync(Arg.Do<OrderNotification>(n => recorded = n), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<OrderNotification>());

        var service = CreateService();
        var result = await service.ResendAsync(1, "fresh-key");

        Assert.Equal(ResendOutcome.Resent, result.Outcome);
        Assert.NotNull(recorded);
        Assert.Equal("SMresend", recorded!.ProviderMessageSid);
        Assert.Equal("fresh-key", recorded.IdempotencyKey);
        Assert.Equal(original.Id, recorded.ResendOfNotificationId);
    }
}
