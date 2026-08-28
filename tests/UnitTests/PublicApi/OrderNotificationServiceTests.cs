using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi;

public sealed class OrderNotificationServiceTests
{
    [Fact]
    public async Task ProviderFailureDoesNotRollBackOrder()
    {
        await using var db = NewContext();
        var gateway = Substitute.For<ITwilioMessagingGateway>();
        await SeedCatalogAndContactAsync(db);
        gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProviderMessage>>(_ => throw new TwilioProviderException("unavailable"));
        var service = Service(db, gateway);

        var order = await service.PlaceOrderAsync("shopper", Request(), default);

        Assert.True(await db.Orders.AnyAsync(x => x.Id == order.Id));
        var notification = await db.OrderNotifications.SingleAsync();
        Assert.Equal(NotificationLocalOutcome.ProviderCallFailed, notification.LocalOutcome);
    }

    [Fact]
    public async Task DispatchSchedulesAtProviderAndCancelCallsItOff()
    {
        await using var db = NewContext();
        var gateway = SuccessfulGateway();
        await SeedCatalogAndContactAsync(db);
        var service = Service(db, gateway);
        var order = await service.PlaceOrderAsync("shopper", Request(), default);

        await service.DispatchAsync(order.Id, default);
        var followUp = await db.OrderNotifications.SingleAsync(x => x.Kind == NotificationKind.DeliveryFollowUp);
        Assert.NotNull(followUp.ScheduledFor);
        Assert.InRange(followUp.ScheduledFor!.Value, DateTimeOffset.UtcNow.AddDays(2.9), DateTimeOffset.UtcNow.AddDays(3.1));

        await service.CancelAsync(order.Id, default);

        await gateway.Received(1).CancelScheduledAsync(followUp.ProviderMessageId!, Arg.Any<CancellationToken>());
        Assert.Equal("canceled", followUp.ProviderStatus);
    }

    [Fact]
    public async Task SameResendKeyCreatesAndSendsOnlyOneNotification()
    {
        await using var db = NewContext();
        var gateway = Substitute.For<ITwilioMessagingGateway>();
        await SeedCatalogAndContactAsync(db);
        gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProviderMessage>>(_ => throw new TwilioProviderException("unavailable"));
        var service = Service(db, gateway);
        await service.PlaceOrderAsync("shopper", Request(), default);
        var original = await db.OrderNotifications.SingleAsync();
        gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(NewProviderMessage("resent"));

        var first = await service.ResendAsync(original.Id, "same-key", default);
        var second = await service.ResendAsync(original.Id, "same-key", default);

        Assert.Equal(first, second);
        Assert.Equal(2, await db.OrderNotifications.CountAsync());
        await gateway.Received(2).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposalClearsLocalContentOnlyAfterProviderConfirms()
    {
        await using var db = NewContext();
        var gateway = SuccessfulGateway();
        await SeedCatalogAndContactAsync(db);
        var service = Service(db, gateway);
        await service.PlaceOrderAsync("shopper", Request(), default);
        var notification = await db.OrderNotifications.SingleAsync();
        gateway.RedactContentAsync(notification.ProviderMessageId!, Arg.Any<CancellationToken>())
            .Returns(NewProviderMessage(notification.ProviderMessageId!, body: null));

        Assert.True(await service.DisposeContentAsync(notification.Id, default));

        Assert.Null(notification.Content);
        Assert.NotNull(notification.ContentDisposedAt);
        await gateway.Received(1).RedactContentAsync(notification.ProviderMessageId!, Arg.Any<CancellationToken>());
    }

    private static CatalogContext NewContext() => new(new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static async Task SeedCatalogAndContactAsync(CatalogContext db)
    {
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 10m, "picture.png"));
        db.ContactNumbers.Add(new ContactNumber("shopper", "+15555550100", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private static PlaceOrderRequest Request() => new([new PlaceOrderItemRequest(1, 2)]);

    private static OrderNotificationService Service(CatalogContext db, ITwilioMessagingGateway gateway) =>
        new(db, gateway, NullLogger<OrderNotificationService>.Instance);

    private static ITwilioMessagingGateway SuccessfulGateway()
    {
        var gateway = Substitute.For<ITwilioMessagingGateway>();
        var sequence = 0;
        gateway.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(_ => NewProviderMessage($"message-{Interlocked.Increment(ref sequence)}"));
        gateway.FetchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => NewProviderMessage(call.ArgAt<string>(0)));
        gateway.CancelScheduledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => NewProviderMessage(call.ArgAt<string>(0), "canceled"));
        return gateway;
    }

    private static ProviderMessage NewProviderMessage(string sid, string status = "queued", string? body = "content") =>
        new(sid, status, null, null, null, null, null, "outbound-api", "+10000000000", "+15555550100", body);
}
