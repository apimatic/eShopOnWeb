using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.NotificationDispatcherTests;

public class SendNewNeverThrows
{
    private readonly ISmsProvider _provider = Substitute.For<ISmsProvider>();
    private readonly IRepository<SmsNotification> _repo = Substitute.For<IRepository<SmsNotification>>();
    private readonly IAppLogger<NotificationDispatcher> _logger = Substitute.For<IAppLogger<NotificationDispatcher>>();

    private NotificationDispatcher Dispatcher() => new(_provider, _repo, _logger);

    private static SmsNotification NewNotification() =>
        new(orderId: 1, buyerId: "buyer1", toNumber: "+18254751588", body: "hi", kind: NotificationKind.OrderPlaced);

    [Fact]
    public async Task ProviderFailureIsRecordedAsSendErrorAndStillPersisted()
    {
        _provider.SendAsync(Arg.Any<SmsSendCommand>(), Arg.Any<CancellationToken>())
            .Returns<SmsSendResult>(_ => throw new InvalidOperationException("boom"));
        _repo.AddAsync(Arg.Any<SmsNotification>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<SmsNotification>());

        var notification = NewNotification();

        // Must not throw: a send failure never fails the order operation.
        var result = await Dispatcher().SendNewAsync(notification);

        Assert.Equal(NotificationStatus.SendError, result.Status);
        Assert.Null(result.ProviderMessageId);
        await _repo.Received(1).AddAsync(notification, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptedSendRecordsProviderIdAndStatus()
    {
        _provider.SendAsync(Arg.Any<SmsSendCommand>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult(true, "SM123", "queued", null, null));
        _repo.AddAsync(Arg.Any<SmsNotification>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<SmsNotification>());

        var result = await Dispatcher().SendNewAsync(NewNotification());

        Assert.Equal("SM123", result.ProviderMessageId);
        Assert.Equal(NotificationStatus.Queued, result.Status);
    }

    [Fact]
    public async Task DeclinedSendIsRecordedWithoutProviderId()
    {
        _provider.SendAsync(Arg.Any<SmsSendCommand>(), Arg.Any<CancellationToken>())
            .Returns(new SmsSendResult(false, null, null, 21211, "Invalid 'To'"));
        _repo.AddAsync(Arg.Any<SmsNotification>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<SmsNotification>());

        var result = await Dispatcher().SendNewAsync(NewNotification());

        Assert.Equal(NotificationStatus.SendError, result.Status);
        Assert.Equal(21211, result.ErrorCode);
    }
}
