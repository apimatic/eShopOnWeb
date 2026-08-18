using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SmsNotifications;

public class NotificationServiceTests
{
    private const string Owner = "demouser@microsoft.com";
    private const string Number = "+15145551234";

    private readonly IRepository<OrderNotification> _notifications = Substitute.For<IRepository<OrderNotification>>();
    private readonly ISmsSender _sms = Substitute.For<ISmsSender>();
    private readonly IAppLogger<NotificationService> _logger = Substitute.For<IAppLogger<NotificationService>>();

    private NotificationService CreateService() => new(_notifications, _sms, _logger);

    private static OrderNotification Notification(int id, NotificationKind kind, string? sid, string? key = null)
    {
        var n = new OrderNotification(1, Owner, kind, Number, idempotencyKey: key).WithId(id);
        if (sid is not null) n.RecordAccepted(sid, MessageDeliveryStatuses.Undelivered);
        return n;
    }

    [Fact]
    public async Task ResendUnderAnAlreadyUsedKeyDoesNotSendASecondMessage()
    {
        var priorResend = Notification(7, NotificationKind.OrderPlaced, "SMprior", key: "key-1");
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(priorResend);

        var result = await CreateService().ResendAsync(3, "key-1");

        Assert.Same(priorResend, result);
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendUnderAFreshKeySendsAndRecordsTheKey()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(3, Arg.Any<CancellationToken>())
            .Returns(Notification(3, NotificationKind.OrderPlaced, "SMoriginal"));
        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMresent", MessageDeliveryStatuses.Queued));

        OrderNotification? added = null;
        _notifications.When(r => r.AddAsync(Arg.Any<OrderNotification>(), Arg.Any<CancellationToken>()))
            .Do(ci => added = ci.Arg<OrderNotification>());

        var result = await CreateService().ResendAsync(3, "key-2");

        await _sms.Received(1).SendAsync(Number, Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.NotNull(added);
        Assert.Equal("key-2", added!.IdempotencyKey);
        Assert.Equal("SMresent", added.MessageSid);
        Assert.Same(added, result);
    }

    [Fact]
    public async Task ResendOfAnUnknownNotificationReturnsNull()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<OrderNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((OrderNotification?)null);
        _notifications.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((OrderNotification?)null);

        Assert.Null(await CreateService().ResendAsync(99, "key-3"));
    }

    [Fact]
    public async Task ReconcileLinesUpBothSides()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow.AddDays(1);

        _sms.ListSentFromConfiguredNumberAsync(from, to, Arg.Any<CancellationToken>())
            .Returns(new List<ProviderMessage>
            {
                new("SMa", "delivered", "+15005550006", DateTimeOffset.UtcNow, null),
                new("SMc", "delivered", "+15005550006", DateTimeOffset.UtcNow, null) // provider knows, eShop doesn't
            });

        var matchedNotification = Notification(1, NotificationKind.OrderPlaced, "SMa");
        var eShopOnlyNotification = Notification(2, NotificationKind.OrderDispatched, "SMb"); // eShop knows, provider doesn't
        _notifications.ListAsync(Arg.Any<OrderNotificationsSentInRangeSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<OrderNotification> { matchedNotification, eShopOnlyNotification });

        var report = await CreateService().ReconcileAsync(from, to);

        Assert.Equal("SMa", Assert.Single(report.Matched).ProviderMessage.Sid);
        Assert.Equal("SMc", Assert.Single(report.ProviderOnly).Sid);
        Assert.Equal("SMb", Assert.Single(report.EShopOnly).MessageSid);
    }
}
