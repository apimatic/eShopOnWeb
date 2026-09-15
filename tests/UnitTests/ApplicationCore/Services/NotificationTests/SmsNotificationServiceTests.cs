using System;
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

public class SmsNotificationServiceTests
{
    private const string Owner = "buyer@example.com";

    private readonly IRepository<SmsNotification> _notifications = Substitute.For<IRepository<SmsNotification>>();
    private readonly IReadRepository<Order> _orders = Substitute.For<IReadRepository<Order>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IAppLogger<SmsNotificationService> _logger = Substitute.For<IAppLogger<SmsNotificationService>>();

    private SmsNotificationService CreateService() => new(_notifications, _orders, _gateway, _logger);

    private static SmsNotification Sent(int orderId, string sid, string status)
    {
        var n = new SmsNotification(Owner, orderId, NotificationKind.OrderDispatched, "+15550000001", "body");
        n.RecordProviderResult(sid, status, null, null, DateTimeOffset.UtcNow);
        return n;
    }

    // ---- Resend idempotency ----

    [Fact]
    public async Task ResendUnderExistingKeyReturnsSameMessageWithoutSending()
    {
        var already = Sent(1, "SM_prev", "queued");
        _notifications.FirstOrDefaultAsync(Arg.Any<SmsNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(already);

        var result = await CreateService().ResendAsync(5, "key-1");

        Assert.Same(already, result);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<SmsNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendUnderNewKeySendsAndRecordsResend()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<SmsNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((SmsNotification?)null);
        var original = Sent(1, "SM_orig", "undelivered");
        _notifications.FirstOrDefaultAsync(Arg.Any<SmsNotificationByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(original);
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsDispatchResult("SM_new", "queued", null, null, null));

        var result = await CreateService().ResendAsync(1, "fresh-key");

        Assert.Equal("SM_new", result.ProviderSid);
        Assert.Equal("fresh-key", result.IdempotencyKey);
        await _gateway.Received(1).SendAsync("+15550000001", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).AddAsync(Arg.Any<SmsNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendOfMissingNotificationThrows()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<SmsNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((SmsNotification?)null);
        _notifications.FirstOrDefaultAsync(Arg.Any<SmsNotificationByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns((SmsNotification?)null);

        await Assert.ThrowsAsync<NotificationNotFoundException>(() => CreateService().ResendAsync(99, "k"));
    }

    // ---- Content disposal ----

    [Fact]
    public async Task DisposeContentRedactsAtProviderAndMarksDisposed()
    {
        var notification = Sent(1, "SM_x", "delivered");
        _notifications.FirstOrDefaultAsync(Arg.Any<SmsNotificationByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(notification);

        await CreateService().DisposeContentAsync(1);

        await _gateway.Received(1).DisposeContentAsync("SM_x", Arg.Any<CancellationToken>());
        Assert.True(notification.ContentDisposed);
        Assert.Null(notification.Body);
        await _notifications.Received(1).UpdateAsync(notification, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeContentSkipsProviderWhenNeverSent()
    {
        // No provider sid: nothing to redact at the provider, but still disposed locally.
        var notification = new SmsNotification(Owner, 1, NotificationKind.OrderPlaced, "+15550000001", "body");
        notification.RecordSendFailure("boom");
        _notifications.FirstOrDefaultAsync(Arg.Any<SmsNotificationByIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(notification);

        await CreateService().DisposeContentAsync(1);

        await _gateway.DidNotReceive().DisposeContentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.True(notification.ContentDisposed);
    }

    // ---- Reconciliation ----

    [Fact]
    public async Task ReconcileClassifiesMatchedProviderOnlyAndEShopOnly()
    {
        _gateway.SendingNumber.Returns("+1999");
        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddHours(1);

        // Provider knows about SM_match (also in eShop) and SM_provOnly (eShop never recorded).
        _gateway.ListSentMessagesAsync(from, to, Arg.Any<CancellationToken>()).Returns(new List<ProviderMessage>
        {
            new("SM_match", "delivered", null, "+1999", DateTimeOffset.UtcNow, null, null),
            new("SM_provOnly", "delivered", null, "+1999", DateTimeOffset.UtcNow, null, null)
        });

        // eShop records: the match, a genuinely eShop-only SENT message, and a cancelled follow-up
        // (never sent) that must NOT be reported as eShop-only.
        var cancelledFollowUp = new SmsNotification(Owner, 1, NotificationKind.DeliveryFollowUp, "+15550000001", "body");
        cancelledFollowUp.RecordProviderResult("SM_cancelled", "canceled", null, null, null);
        _notifications.ListAsync(Arg.Any<SmsNotificationsCreatedBetweenSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<SmsNotification>
            {
                Sent(1, "SM_match", "delivered"),
                Sent(2, "SM_eshopOnly", "delivered"),
                cancelledFollowUp
            });

        var report = await CreateService().ReconcileAsync(from, to);

        Assert.Equal("+1999", report.FromNumber);
        Assert.Single(report.Matched);
        Assert.Equal("SM_match", report.Matched[0].ProviderSid);
        Assert.Single(report.ProviderOnly);
        Assert.Equal("SM_provOnly", report.ProviderOnly[0].ProviderSid);
        Assert.Single(report.EShopOnly);
        Assert.Equal("SM_eshopOnly", report.EShopOnly[0].ProviderSid);
        Assert.False(report.InSync);
    }
}
