using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.NotificationServiceTests;

public class ResendNotificationTests
{
    private readonly IRepository<Notification> _notifications = Substitute.For<IRepository<Notification>>();
    private readonly ISmsProvider _sms = Substitute.For<ISmsProvider>();
    private readonly IAppLogger<NotificationAdminService> _logger = Substitute.For<IAppLogger<NotificationAdminService>>();

    private NotificationAdminService CreateService() => new(_notifications, _sms, _logger);

    [Fact]
    public async Task RepeatUnderSameKeyDoesNotSendAgain()
    {
        // A notification already exists under this idempotency key.
        var existing = new Notification(1, "buyer", "+15005550006", "hello", NotificationKind.OrderPlaced, "key-A");
        _notifications
            .FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await CreateService().ResendAsync(99, "key-A");

        Assert.Equal(ResendOutcome.Duplicate, result.Outcome);
        // No second message is sent, and no new record is added.
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FreshKeySendsAndRecordsNewNotification()
    {
        _notifications
            .FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((Notification?)null);
        var original = new Notification(1, "buyer", "+15005550006", "hello", NotificationKind.OrderPlaced);
        _notifications.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(original);
        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SentMessage("SMxyz", NotificationDeliveryStatus.Queued, null));

        var result = await CreateService().ResendAsync(5, "fresh-key");

        Assert.Equal(ResendOutcome.Sent, result.Outcome);
        await _sms.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.Received().AddAsync(
            Arg.Is<Notification>(n => n.IdempotencyKey == "fresh-key"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposedContentCannotBeResent()
    {
        _notifications
            .FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((Notification?)null);
        var disposed = new Notification(1, "buyer", "+15005550006", "hello", NotificationKind.OrderPlaced);
        disposed.DisposeContent();
        _notifications.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(disposed);

        var result = await CreateService().ResendAsync(7, "any-key");

        Assert.Equal(ResendOutcome.ContentDisposed, result.Outcome);
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingNotificationReturnsNotFound()
    {
        _notifications
            .FirstOrDefaultAsync(Arg.Any<NotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((Notification?)null);
        _notifications.GetByIdAsync(123, Arg.Any<CancellationToken>()).Returns((Notification?)null);

        var result = await CreateService().ResendAsync(123, "any-key");

        Assert.Equal(ResendOutcome.NotFound, result.Outcome);
    }
}
