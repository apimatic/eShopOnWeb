using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.NotificationTests;

public class OrderNotificationServiceTests
{
    private const string Buyer = "buyer@example.com";

    private readonly IReadRepository<ContactNumber> _contactNumbers = Substitute.For<IReadRepository<ContactNumber>>();
    private readonly IRepository<SmsNotification> _notifications = Substitute.For<IRepository<SmsNotification>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();

    private OrderNotificationService CreateService() => new(_contactNumbers, _notifications, _gateway, _logger);

    private static Order NewOrder() =>
        new(Buyer, new Address("1 St", "City", "ST", "Country", "00000"), new List<OrderItem>());

    private void OwnerHasNumbers(params string[] numbers)
    {
        var list = new List<ContactNumber>();
        foreach (var n in numbers) list.Add(new ContactNumber(Buyer, n));
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByOwnerSpecification>(), Arg.Any<CancellationToken>()).Returns(list);
    }

    [Fact]
    public async Task ShopperWithNoNumberIsNotMessaged()
    {
        OwnerHasNumbers(); // none

        await CreateService().NotifyOrderPlacedAsync(NewOrder());

        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<SmsNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlacedMessageRecordsProviderResult()
    {
        OwnerHasNumbers("+15550000001");
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsDispatchResult("SM123", "queued", null, null, null));

        await CreateService().NotifyOrderPlacedAsync(NewOrder());

        await _notifications.Received(1).AddAsync(
            Arg.Is<SmsNotification>(n =>
                n.Kind == NotificationKind.OrderPlaced &&
                n.ProviderSid == "SM123" &&
                n.ProviderStatus == "queued"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendFailureDoesNotThrowAndIsRecordedAsFailed()
    {
        OwnerHasNumbers("+15550000001");
        _gateway.When(g => g.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new SmsGatewayException("refused", 30006));

        // Must not throw — the order operation still succeeds.
        await CreateService().NotifyOrderPlacedAsync(NewOrder());

        await _notifications.Received(1).AddAsync(
            Arg.Is<SmsNotification>(n => n.ProviderStatus == "failed" && n.ProviderErrorCode == 30006),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchSchedulesFollowUp()
    {
        OwnerHasNumbers("+15550000001");
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsDispatchResult("SM_send", "queued", null, null, null));
        _gateway.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new SmsDispatchResult("SM_sched", "scheduled", null, null, null));

        await CreateService().NotifyOrderDispatchedAsync(NewOrder());

        await _gateway.Received(1).ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<System.DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).AddAsync(
            Arg.Is<SmsNotification>(n => n.Kind == NotificationKind.DeliveryFollowUp && n.ProviderSid == "SM_sched"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelCallsOffPendingFollowUps()
    {
        OwnerHasNumbers(); // no new sends needed for this assertion

        var followUp = new SmsNotification(Buyer, 7, NotificationKind.DeliveryFollowUp, "+15550000001", "body");
        followUp.RecordProviderResult("SM_sched", "scheduled", null, null, null);
        _notifications.ListAsync(Arg.Any<PendingFollowUpsByOrderSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<SmsNotification> { followUp });

        await CreateService().NotifyOrderCancelledAsync(NewOrder());

        await _gateway.Received(1).CancelScheduledAsync("SM_sched", Arg.Any<CancellationToken>());
        await _notifications.Received().UpdateAsync(
            Arg.Is<SmsNotification>(n => n.ProviderStatus == "canceled"), Arg.Any<CancellationToken>());
    }
}
