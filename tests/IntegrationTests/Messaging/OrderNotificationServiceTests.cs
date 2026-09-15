using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Messaging;

public sealed class OrderNotificationServiceTests
{
    [Fact]
    public async Task RegistrationStoresProviderCanonicalNumberAndScopesItToOwner()
    {
        await using var db = NewContext();
        var gateway = Substitute.For<ITwilioMessagingGateway>();
        gateway.ValidatePhoneNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+14165550123"));
        var service = NewService(db, gateway);

        var id = await service.RegisterContactNumberAsync("shopper-a", "(416) 555-0123", default);

        var own = await service.GetContactNumbersAsync("shopper-a", default);
        var other = await service.GetContactNumbersAsync("shopper-b", default);
        Assert.Equal(id, Assert.Single(own).ContactNumberId);
        Assert.Equal("+14165550123", own[0].PhoneNumber);
        Assert.Empty(other);
        await Assert.ThrowsAsync<NotificationOperationException>(() =>
            service.DeleteContactNumberAsync("shopper-b", id, default));
    }

    [Fact]
    public async Task ProviderSendFailureDoesNotRollBackOrder()
    {
        await using var db = NewContext();
        await SeedCatalogAsync(db);
        var gateway = Substitute.For<ITwilioMessagingGateway>();
        gateway.ValidatePhoneNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+14165550123"));
        gateway.SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>())
            .Returns(new ProviderMessageResult(false, null, "Failed"));
        var service = NewService(db, gateway);
        await service.RegisterContactNumberAsync("shopper", "+14165550123", default);

        var orderId = await service.PlaceOrderAsync("shopper", [new OrderLineInput(1, 2)], Address(), default);

        Assert.NotNull(await db.Orders.FindAsync(orderId));
        var notification = Assert.Single(await db.OrderNotifications.ToListAsync());
        Assert.Equal("Failed", notification.ProviderStatus);
    }

    [Fact]
    public async Task DispatchSchedulesProviderFollowUpAndCancelCallsItOff()
    {
        await using var db = NewContext();
        await SeedCatalogAsync(db);
        var gateway = SuccessfulGateway();
        var service = NewService(db, gateway);
        await service.RegisterContactNumberAsync("shopper", "+14165550123", default);
        var orderId = await service.PlaceOrderAsync("shopper", [new OrderLineInput(1, 1)], Address(), default);

        await service.DispatchOrderAsync(orderId, default);
        var followUp = await db.OrderNotifications.SingleAsync(x => x.Kind == NotificationKind.DeliveryFollowUp);
        Assert.Equal("scheduled", followUp.ProviderStatus);
        Assert.InRange(followUp.ScheduledFor!.Value, DateTimeOffset.UtcNow.AddDays(2.9), DateTimeOffset.UtcNow.AddDays(3.1));

        await service.CancelOrderAsync(orderId, default);

        await gateway.Received(1).CancelScheduledMessageAsync(followUp.ProviderMessageSid!, Arg.Any<CancellationToken>());
        Assert.Equal("canceled", followUp.ProviderStatus);
    }

    [Fact]
    public async Task SameResendIdempotencyKeyReturnsSameNotificationWithoutSecondSend()
    {
        await using var db = NewContext();
        await SeedCatalogAsync(db);
        var gateway = SuccessfulGateway("undelivered");
        var service = NewService(db, gateway);
        await service.RegisterContactNumberAsync("shopper", "+14165550123", default);
        var orderId = await service.PlaceOrderAsync("shopper", [new OrderLineInput(1, 1)], Address(), default);
        var source = await db.OrderNotifications.SingleAsync(x => x.OrderId == orderId);
        var sendsBefore = await gateway.ReceivedCalls().CountAsync();

        var first = await service.ResendAsync(source.Id, "same-key", default);
        var second = await service.ResendAsync(source.Id, "same-key", default);
        var fresh = await service.ResendAsync(source.Id, "fresh-key", default);

        Assert.Equal(first, second);
        Assert.NotEqual(first, fresh);
        Assert.Equal(3, await db.OrderNotifications.CountAsync());
        Assert.Equal(sendsBefore + 4, await gateway.ReceivedCalls().CountAsync()); // two fetches + two provider sends
    }

    [Fact]
    public async Task ContentIsClearedLocallyOnlyAfterProviderRedactionIsVerified()
    {
        await using var db = NewContext();
        await SeedCatalogAsync(db);
        var gateway = SuccessfulGateway();
        var service = NewService(db, gateway);
        await service.RegisterContactNumberAsync("shopper", "+14165550123", default);
        var orderId = await service.PlaceOrderAsync("shopper", [new OrderLineInput(1, 1)], Address(), default);
        var notification = await db.OrderNotifications.SingleAsync(x => x.OrderId == orderId);
        var redacted = Snapshot(notification.ProviderMessageSid!, "delivered", DateTimeOffset.UtcNow) with { Body = string.Empty };
        gateway.DisposeMessageContentAsync(notification.ProviderMessageSid!, Arg.Any<CancellationToken>()).Returns(redacted);
        gateway.FetchMessageAsync(notification.ProviderMessageSid!, Arg.Any<CancellationToken>()).Returns(redacted);

        await service.DisposeContentAsync(notification.Id, default);

        Assert.Null(notification.Body);
        Assert.NotNull(notification.ContentDisposedAt);
        await gateway.Received(1).DisposeMessageContentAsync(notification.ProviderMessageSid!, Arg.Any<CancellationToken>());
        await gateway.Received(1).FetchMessageAsync(notification.ProviderMessageSid!, Arg.Any<CancellationToken>());
    }

    private static CatalogContext NewContext() => new(new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static OrderNotificationService NewService(CatalogContext db, ITwilioMessagingGateway gateway) =>
        new(db, gateway, new NotificationLockRegistry(), TimeProvider.System);

    private static ShippingAddressInput Address() => new("1 Main St", "Toronto", "ON", "Canada", "M5V 1A1");

    private static async Task SeedCatalogAsync(CatalogContext db)
    {
        db.CatalogItems.Add(new CatalogItem(1, 1, "Description", "Item", 10m, "item.png"));
        await db.SaveChangesAsync();
    }

    private static ITwilioMessagingGateway SuccessfulGateway(string immediateStatus = "delivered")
    {
        var gateway = Substitute.For<ITwilioMessagingGateway>();
        gateway.ValidatePhoneNumberAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PhoneValidationResult(true, "+14165550123"));
        var sequence = 0;
        gateway.SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var scheduled = call.ArgAt<DateTimeOffset?>(2);
                var status = scheduled is null ? immediateStatus : "scheduled";
                var snapshot = Snapshot($"SM{Interlocked.Increment(ref sequence):D32}", status,
                    scheduled is null ? DateTimeOffset.UtcNow : null);
                return new ProviderMessageResult(true, snapshot, status);
            });
        gateway.FetchMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Snapshot(call.ArgAt<string>(0), immediateStatus, DateTimeOffset.UtcNow));
        gateway.CancelScheduledMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Snapshot(call.ArgAt<string>(0), "canceled", null));
        return gateway;
    }

    private static ProviderMessageSnapshot Snapshot(string sid, string status, DateTimeOffset? sentAt) =>
        new(sid, status, "body", null, DateTimeOffset.UtcNow, sentAt, DateTimeOffset.UtcNow, "outbound-api");
}

internal static class CallCountExtensions
{
    public static Task<int> CountAsync(this IEnumerable<NSubstitute.Core.ICall> calls) => Task.FromResult(calls.Count());
}
