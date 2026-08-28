#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Messaging;

public class OrderNotificationServiceTests
{
    [Fact]
    public async Task DispatchSchedulesFollowUpAndCancelCancelsItAtProvider()
    {
        await using var db = CreateContext();
        var provider = Substitute.For<ITwilioMessagingClient>();
        provider.ValidatePhoneNumberAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ValidatedPhoneNumber(true, "+15550000001", Array.Empty<string>()));
        var sequence = 0;
        provider.SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var scheduled = call.ArgAt<DateTimeOffset?>(2).HasValue;
                return new ProviderMessage($"SM{Interlocked.Increment(ref sequence):D32}", scheduled ? "scheduled" : "queued", null, DateTimeOffset.UtcNow, null);
            });
        provider.CancelMessageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new ProviderMessage(call.ArgAt<string>(0), "canceled", null, DateTimeOffset.UtcNow, null));

        var service = new OrderNotificationService(db, provider, new NotificationIdempotencyLock());
        await SeedCatalogItemAsync(db);
        await service.RegisterContactNumberAsync("shopper", "typed-value", null, CancellationToken.None);
        var orderId = await service.PlaceOrderAsync(
            "shopper",
            new[] { new OrderLineInput(1, 2) },
            Address(),
            CancellationToken.None);

        Assert.Equal(OperationOutcome.Success, (await service.DispatchOrderAsync(orderId, CancellationToken.None)).Outcome);
        var followUp = await db.OrderNotifications.SingleAsync(x => x.Kind == NotificationKind.DeliveryFollowUp);
        Assert.Equal("scheduled", followUp.ProviderStatus);

        Assert.Equal(OperationOutcome.Success, (await service.CancelOrderAsync(orderId, CancellationToken.None)).Outcome);
        Assert.Equal("canceled", followUp.ProviderStatus);
        await provider.Received(1).CancelMessageAsync(followUp.ProviderMessageSid!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResendUsesCallerKeyAndAllowsFreshKey()
    {
        await using var db = CreateContext();
        var provider = Substitute.For<ITwilioMessagingClient>();
        provider.FetchMessageAsync("SMoriginal", Arg.Any<CancellationToken>())
            .Returns(new ProviderMessage("SMoriginal", "undelivered", 30005, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var sends = 0;
        provider.SendMessageAsync(Arg.Any<string>(), Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(_ => new ProviderMessage($"SMresend{Interlocked.Increment(ref sends):D26}", "queued", null, DateTimeOffset.UtcNow, null));

        var order = new Order("shopper", new Address("street", "city", "state", "country", "zip"), new List<OrderItem>());
        var contact = new ContactNumber("shopper", "+15550000001", DateTimeOffset.UtcNow);
        db.AddRange(order, contact);
        await db.SaveChangesAsync();
        var original = new OrderNotification(
            order.Id,
            "shopper",
            contact.Id,
            NotificationKind.OrderPlaced,
            "message body",
            DateTimeOffset.UtcNow);
        original.RecordProviderAcceptance("SMoriginal", "undelivered", 30005, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        db.OrderNotifications.Add(original);
        await db.SaveChangesAsync();

        var service = new OrderNotificationService(db, provider, new NotificationIdempotencyLock());
        var first = await service.ResendNotificationAsync(original.Id, "key-one", CancellationToken.None);
        var repeated = await service.ResendNotificationAsync(original.Id, "key-one", CancellationToken.None);
        var fresh = await service.ResendNotificationAsync(original.Id, "key-two", CancellationToken.None);

        Assert.Equal(first.Identifier, repeated.Identifier);
        Assert.NotEqual(first.Identifier, fresh.Identifier);
        Assert.Equal(2, sends);
    }

    private static CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CatalogContext(options);
    }

    private static async Task SeedCatalogItemAsync(CatalogContext db)
    {
        db.CatalogItems.Add(new CatalogItem(1, 1, "description", "item", 12.34m, "picture.png"));
        await db.SaveChangesAsync();
    }

    private static ShippingAddressInput Address() => new("street", "city", "state", "country", "zip");
}
