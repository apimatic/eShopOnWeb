using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.NotificationTests;

public class NotificationOperationsServiceTests
{
    private readonly IRepository<SmsNotification> _repo = Substitute.For<IRepository<SmsNotification>>();
    private readonly ISmsGateway _gateway = Substitute.For<ISmsGateway>();
    private readonly IAppLogger<NotificationOperationsService> _logger = Substitute.For<IAppLogger<NotificationOperationsService>>();

    private NotificationOperationsService CreateService() => new(_repo, _gateway, _logger);

    private static SmsNotification SentNotification(string sid = "SMoriginal")
    {
        var n = new SmsNotification("buyer", 1, NotificationType.OrderDispatched, "+15551234567", "on its way");
        n.RecordProviderAccepted(sid, SmsDeliveryStatus.Undelivered, 30034, null);
        return n;
    }

    [Fact]
    public async Task RepeatingResendUnderSameKeyDoesNotSendAgain()
    {
        var prior = SentNotification("SMprior");
        _repo.FirstOrDefaultAsync(Arg.Any<SmsNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns(prior);
        var service = CreateService();

        var outcome = await service.ResendAsync(1, "key-123");

        Assert.True(outcome.Replayed);
        Assert.Same(prior, outcome.Result);
        await _gateway.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FreshKeyGenuinelySendsAgain()
    {
        _repo.FirstOrDefaultAsync(Arg.Any<SmsNotificationByIdempotencyKeySpecification>(), Arg.Any<CancellationToken>())
            .Returns((SmsNotification?)null);
        _repo.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(SentNotification());
        _repo.AddAsync(Arg.Any<SmsNotification>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<SmsNotification>());
        _gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SentSmsMessage("SMnew", SmsDeliveryStatus.Queued, null, null));
        var service = CreateService();

        var outcome = await service.ResendAsync(1, "fresh-key");

        Assert.False(outcome.Replayed);
        Assert.Equal("SMnew", outcome.Result!.ProviderMessageSid);
        Assert.Equal("fresh-key", outcome.Result!.IdempotencyKey);
        await _gateway.Received().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposingContentRedactsAtProviderAndKeepsTheRecord()
    {
        var notification = SentNotification("SMredact");
        _repo.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(notification);
        var service = CreateService();

        var disposed = await service.DisposeContentAsync(7);

        Assert.True(disposed);
        await _gateway.Received().RedactContentAsync("SMredact", Arg.Any<CancellationToken>());
        Assert.True(notification.ContentRedacted);
        Assert.Null(notification.Body);
        Assert.Equal(SmsDeliveryStatus.Undelivered, notification.Status); // outcome survives
    }
}
