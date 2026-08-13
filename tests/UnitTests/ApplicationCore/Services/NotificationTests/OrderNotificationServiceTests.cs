using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.NotificationTests;

public class OrderNotificationServiceTests
{
    private const string BuyerId = "buyer@example.com";
    private readonly IRepository<ContactNumber> _contactNumbers = Substitute.For<IRepository<ContactNumber>>();
    private readonly IRepository<Notification> _notifications = Substitute.For<IRepository<Notification>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IAppLogger<OrderNotificationService> _logger = Substitute.For<IAppLogger<OrderNotificationService>>();
    private readonly TwilioSettings _settings = new() { FollowUpDelay = TimeSpan.FromDays(3) };

    private OrderNotificationService CreateSut() => new(_contactNumbers, _notifications, _gateway, _settings, _logger);

    private static Order NewOrder() => new(BuyerId, new Address("s", "c", "st", "co", "z"), new List<OrderItem>());

    private void HasNumbers(params string[] numbers)
    {
        var list = new List<ContactNumber>();
        foreach (var n in numbers) list.Add(new ContactNumber(BuyerId, n));
        _contactNumbers.ListAsync(Arg.Any<ContactNumbersByBuyerSpecification>(), Arg.Any<CancellationToken>()).Returns(list);
    }

    [Fact]
    public async Task PlacedWithNoNumberOnFile_SendsNothingAndStoresNothing()
    {
        HasNumbers(); // none
        var sut = CreateSut();

        await sut.NotifyOrderPlacedAsync(NewOrder());

        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Placed_SendsToEveryRegisteredNumber()
    {
        HasNumbers("+15550000001", "+15550000002");
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new SmsSendResult("SMxxx", MessageStatuses.Queued));
        var sut = CreateSut();

        await sut.NotifyOrderPlacedAsync(NewOrder());

        await _gateway.Received(2).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.Received(2).AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendFailure_NeverThrowsAndRecordsFailedNotification()
    {
        HasNumbers("+15550000001");
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns<SmsSendResult>(_ => throw new InvalidOperationException("provider down"));
        Notification? stored = null;
        await _notifications.AddAsync(Arg.Do<Notification>(n => stored = n), Arg.Any<CancellationToken>());
        var sut = CreateSut();

        var ex = await Record.ExceptionAsync(() => sut.NotifyOrderPlacedAsync(NewOrder()));

        Assert.Null(ex); // messaging failure must never fail the operation
        Assert.NotNull(stored);
        Assert.Equal(MessageStatuses.Failed, stored!.Status);
        Assert.Null(stored.ProviderMessageSid);
    }

    [Fact]
    public async Task Dispatched_SendsImmediateAndSchedulesFollowUpInTheFuture()
    {
        HasNumbers("+15550000001");
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new SmsSendResult("SMnow", MessageStatuses.Queued));
        DateTimeOffset scheduledFor = default;
        _gateway.ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Do<DateTimeOffset>(d => scheduledFor = d), Arg.Any<CancellationToken>())
                .Returns(new SmsSendResult("SMlater", MessageStatuses.Scheduled));
        var sut = CreateSut();

        await sut.NotifyOrderDispatchedAsync(NewOrder());

        await _gateway.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _gateway.Received(1).ScheduleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        Assert.True(scheduledFor > DateTimeOffset.UtcNow.AddDays(2), "follow-up should be queued a few days out");
    }

    [Fact]
    public async Task Cancelled_CancelsPendingFollowUpsThenNotifies()
    {
        HasNumbers("+15550000001");
        var pending = new Notification(BuyerId, 7, NotificationKind.DeliveryFollowUp, "+15550000001", "body");
        pending.RecordScheduled("SMsched", MessageStatuses.Scheduled, DateTimeOffset.UtcNow.AddDays(3));
        _notifications.ListAsync(Arg.Any<PendingFollowUpsByOrderSpecification>(), Arg.Any<CancellationToken>())
                      .Returns(new List<Notification> { pending });
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new SmsSendResult("SMcancelmsg", MessageStatuses.Queued));
        var sut = CreateSut();

        await sut.NotifyOrderCancelledAsync(NewOrder());

        await _gateway.Received(1).CancelScheduledAsync("SMsched", Arg.Any<CancellationToken>());
        Assert.Equal(MessageStatuses.Canceled, pending.Status);
        await _gateway.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_RepeatedIdempotencyKey_ReturnsExistingAndDoesNotSendAgain()
    {
        var existing = new Notification(BuyerId, 1, NotificationKind.Resend, "+15550000001", "body");
        existing.RecordSent("SMorig", MessageStatuses.Sent);
        existing.MarkAsResendOf(2, "key-1");
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
                      .Returns(existing);
        var sut = CreateSut();

        var result = await sut.ResendAsync(2, "key-1");

        Assert.Same(existing, result);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_FreshKey_SendsAndPersists()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
                      .Returns((Notification?)null);
        var original = new Notification(BuyerId, 1, NotificationKind.OrderPlaced, "+15550000001", "hello");
        original.RecordSent("SMorig", MessageStatuses.Undelivered);
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdSpecification>(), Arg.Any<CancellationToken>())
                      .Returns(original);
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new SmsSendResult("SMresent", MessageStatuses.Queued));
        var sut = CreateSut();

        var result = await sut.ResendAsync(1, "key-2");

        Assert.Equal("SMresent", result.ProviderMessageSid);
        Assert.Equal("key-2", result.IdempotencyKey);
        await _gateway.Received(1).SendAsync("+15550000001", "hello", Arg.Any<CancellationToken>());
        await _notifications.Received(1).AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_OfDisposedContent_Throws()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
                      .Returns((Notification?)null);
        var original = new Notification(BuyerId, 1, NotificationKind.OrderPlaced, "+15550000001", "hello");
        original.RecordSent("SMorig", MessageStatuses.Undelivered);
        original.MarkContentRedacted();
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdSpecification>(), Arg.Any<CancellationToken>())
                      .Returns(original);
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotificationContentUnavailableException>(() => sut.ResendAsync(1, "key-3"));
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resend_UnknownNotification_Throws()
    {
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
                      .Returns((Notification?)null);
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdSpecification>(), Arg.Any<CancellationToken>())
                      .Returns((Notification?)null);
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotificationNotFoundException>(() => sut.ResendAsync(999, "key-4"));
    }

    [Fact]
    public async Task Redact_DisposesProviderContentThenMarksRedacted()
    {
        var n = new Notification(BuyerId, 1, NotificationKind.OrderPlaced, "+15550000001", "secret text");
        n.RecordSent("SMredact", MessageStatuses.Delivered);
        _notifications.FirstOrDefaultAsync(Arg.Any<NotificationByIdSpecification>(), Arg.Any<CancellationToken>())
                      .Returns(n);
        var sut = CreateSut();

        await sut.RedactContentAsync(1);

        await _gateway.Received(1).RedactContentAsync("SMredact", Arg.Any<CancellationToken>());
        Assert.True(n.ContentRedacted);
        Assert.Null(n.Body); // text no longer retrievable from this app either
        Assert.Equal(MessageStatuses.Delivered, n.Status); // fact + outcome survive
    }

    [Fact]
    public async Task Reconcile_DiffsProviderAgainstLocalBothWays()
    {
        var local = new Notification(BuyerId, 1, NotificationKind.OrderPlaced, "+15550000001", "b");
        local.RecordSent("SM-matched", MessageStatuses.Delivered);
        var localOnly = new Notification(BuyerId, 1, NotificationKind.DeliveryFollowUp, "+15550000001", "b");
        localOnly.RecordScheduled("SM-eshoponly", MessageStatuses.Canceled, DateTimeOffset.UtcNow);
        _notifications.ListAsync(Arg.Any<NotificationsSentInRangeSpecification>(), Arg.Any<CancellationToken>())
                      .Returns(new List<Notification> { local, localOnly });
        _gateway.ListSentMessagesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new List<ProviderMessage>
                {
                    new("SM-matched", "+1999", MessageStatuses.Delivered, DateTimeOffset.UtcNow),
                    new("SM-provideronly", "+1999", "received", DateTimeOffset.UtcNow)
                });
        var sut = CreateSut();

        var report = await sut.ReconcileAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);

        Assert.Equal(1, report.MatchedCount);
        Assert.Equal(1, report.ProviderOnlyCount);
        Assert.Equal(1, report.EShopOnlyCount);
    }

    [Fact]
    public async Task RefreshStatuses_SkipsTerminalAndUpdatesNonTerminal()
    {
        var terminal = new Notification(BuyerId, 1, NotificationKind.OrderPlaced, "+1", "b");
        terminal.RecordSent("SM-term", MessageStatuses.Delivered);
        var pending = new Notification(BuyerId, 1, NotificationKind.OrderPlaced, "+1", "b");
        pending.RecordSent("SM-pending", MessageStatuses.Queued);
        _gateway.GetStatusAsync("SM-pending", Arg.Any<CancellationToken>()).Returns(MessageStatuses.Delivered);
        var sut = CreateSut();

        await sut.RefreshStatusesAsync(new[] { terminal, pending });

        await _gateway.DidNotReceive().GetStatusAsync("SM-term", Arg.Any<CancellationToken>());
        await _gateway.Received(1).GetStatusAsync("SM-pending", Arg.Any<CancellationToken>());
        Assert.Equal(MessageStatuses.Delivered, pending.Status);
    }
}
