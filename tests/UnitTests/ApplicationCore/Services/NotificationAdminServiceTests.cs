using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class NotificationAdminServiceTests
{
    private readonly IRepository<Notification> _repo = Substitute.For<IRepository<Notification>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IAppLogger<NotificationAdminService> _logger = Substitute.For<IAppLogger<NotificationAdminService>>();

    private NotificationAdminService Service() => new(_repo, _gateway, _logger);

    private static Notification SentNotification() =>
        new Notification(1, "buyer@example.com", NotificationKind.OrderPlaced, "+15551234567", "hello");

    [Fact]
    public async Task RepeatingSameIdempotencyKeyReplaysAndDoesNotSendAgain()
    {
        var prior = SentNotification();
        _repo.FirstOrDefaultAsync(Arg.Any<NotificationByResendKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(prior);

        var result = await Service().ResendAsync(notificationId: 5, idempotencyKey: "key-A");

        Assert.Equal(ResendStatus.ReplayedExisting, result.Status);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FreshKeyReservesRecordAndSendsOnce()
    {
        _repo.FirstOrDefaultAsync(Arg.Any<NotificationByResendKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((Notification?)null);
        _repo.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(SentNotification());
        _repo.AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Notification>());
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult("SMxyz", "queued", null, null));

        var result = await Service().ResendAsync(notificationId: 5, idempotencyKey: "key-B");

        Assert.Equal(ResendStatus.Sent, result.Status);
        await _gateway.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        // Record is reserved (persisted) before the send so a repeat/retry cannot double-send.
        await _repo.Received().AddAsync(Arg.Is<Notification>(n => n.ResendIdempotencyKey == "key-B"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendFailureStillReservesKeyAndReportsSent()
    {
        _repo.FirstOrDefaultAsync(Arg.Any<NotificationByResendKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((Notification?)null);
        _repo.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(SentNotification());
        _repo.AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<Notification>());
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SmsSendResult>(_ => throw new SmsGatewayException("boom", 500));

        var result = await Service().ResendAsync(5, "key-C");

        Assert.Equal(ResendStatus.Sent, result.Status);
        await _repo.Received().UpdateAsync(Arg.Is<Notification>(n => n.DeliveryStatus == NotificationDeliveryStatus.SendFailed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendOfUnknownNotificationReportsNotFound()
    {
        _repo.FirstOrDefaultAsync(Arg.Any<NotificationByResendKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((Notification?)null);
        _repo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((Notification?)null);

        var result = await Service().ResendAsync(999, "key-D");

        Assert.Equal(ResendStatus.SourceNotFound, result.Status);
    }

    [Fact]
    public async Task ResendOfDisposedContentIsRefusedWithoutSending()
    {
        var disposed = SentNotification();
        disposed.MarkContentDisposed();
        _repo.FirstOrDefaultAsync(Arg.Any<NotificationByResendKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((Notification?)null);
        _repo.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(disposed);

        var result = await Service().ResendAsync(5, "key-E");

        Assert.Equal(ResendStatus.ContentUnavailable, result.Status);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposeRedactsAtProviderThenMarksDisposedKeepingStatus()
    {
        var notification = SentNotification();
        notification.RecordSent("SMabc", "delivered", null, null);
        _repo.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(notification);

        var result = await Service().DisposeContentAsync(7);

        Assert.Equal(DisposeStatus.Ok, result.Status);
        await _gateway.Received(1).RedactContentAsync("SMabc", Arg.Any<CancellationToken>());
        Assert.True(notification.ContentDisposed);
        Assert.Null(notification.Body);
        Assert.Equal("delivered", notification.DeliveryStatus); // what became of it survives
    }
}
