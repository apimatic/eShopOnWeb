using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Notifications;

public class OrderNotificationServiceTests
{
    private const string Buyer = "buyer@test.com";

    private static CatalogContext NewContext()
        => new(new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static OrderNotificationService NewService(CatalogContext ctx, FakeSmsSender sms)
        => new(sms, new EfRepository<OrderNotification>(ctx), new EfRepository<ContactNumber>(ctx),
            Substitute.For<IAppLogger<OrderNotificationService>>());

    private static async Task<Order> SeedOrderAsync(CatalogContext ctx)
    {
        var order = new Order(Buyer, new Address("1 St", "Town", "ST", "Country", "00000"),
            new List<OrderItem> { new(new CatalogItemOrdered(1, "Widget", "pic.png"), 9.99m, 1) });
        await new EfRepository<Order>(ctx).AddAsync(order);
        return order;
    }

    private static async Task RegisterNumberAsync(CatalogContext ctx, string number)
        => await new EfRepository<ContactNumber>(ctx).AddAsync(new ContactNumber(Buyer, number));

    private static async Task<List<OrderNotification>> NotificationsAsync(CatalogContext ctx, int orderId)
        => (await new EfRepository<OrderNotification>(ctx).ListAsync(new OrderNotificationsByOrderSpecification(orderId))).ToList();

    [Fact]
    public async Task Placed_sends_to_each_registered_number_and_records_provider_sid()
    {
        using var ctx = NewContext();
        var sms = new FakeSmsSender();
        var order = await SeedOrderAsync(ctx);
        await RegisterNumberAsync(ctx, "+15551110000");
        await RegisterNumberAsync(ctx, "+15551110001");

        await NewService(ctx, sms).NotifyOrderPlacedAsync(order, default);

        Assert.Equal(2, sms.Sent.Count);
        var notifications = await NotificationsAsync(ctx, order.Id);
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, n =>
        {
            Assert.Equal(NotificationType.OrderPlaced, n.Type);
            Assert.False(string.IsNullOrEmpty(n.ProviderMessageSid));
            Assert.Equal(NotificationSendState.Accepted, n.SendState);
        });
    }

    [Fact]
    public async Task Placed_with_no_number_on_file_sends_nothing()
    {
        using var ctx = NewContext();
        var sms = new FakeSmsSender();
        var order = await SeedOrderAsync(ctx);

        await NewService(ctx, sms).NotifyOrderPlacedAsync(order, default);

        Assert.Empty(sms.Sent);
        Assert.Empty(await NotificationsAsync(ctx, order.Id));
    }

    [Fact]
    public async Task Placed_when_send_fails_records_failure_and_does_not_throw()
    {
        using var ctx = NewContext();
        var sms = new FakeSmsSender
        {
            OnSend = (_, _) => throw new SmsProviderException("provider said no", HttpStatusCode.BadRequest)
        };
        var order = await SeedOrderAsync(ctx);
        await RegisterNumberAsync(ctx, "+15551110000");

        // Must not throw — the order operation always succeeds.
        await NewService(ctx, sms).NotifyOrderPlacedAsync(order, default);

        var notification = Assert.Single(await NotificationsAsync(ctx, order.Id));
        Assert.Equal(NotificationSendState.Failed, notification.SendState);
        Assert.Null(notification.ProviderMessageSid);
        Assert.Equal("provider said no", notification.ProviderErrorMessage);
    }

    [Fact]
    public async Task Dispatch_tells_shopper_and_schedules_delivery_feedback()
    {
        using var ctx = NewContext();
        var sms = new FakeSmsSender();
        var order = await SeedOrderAsync(ctx);
        await RegisterNumberAsync(ctx, "+15551110000");

        await NewService(ctx, sms).NotifyOrderDispatchedAsync(order, default);

        Assert.Single(sms.Sent);          // "on its way"
        Assert.Single(sms.Scheduled);     // "how did it go?" queued with the provider
        Assert.True(sms.Scheduled[0].SendAt > DateTimeOffset.UtcNow.AddDays(1));

        var notifications = await NotificationsAsync(ctx, order.Id);
        Assert.Contains(notifications, n => n.Type == NotificationType.OrderDispatched && !n.IsScheduled);
        var feedback = Assert.Single(notifications.Where(n => n.Type == NotificationType.DeliveryFeedback));
        Assert.True(feedback.IsScheduled);
        Assert.Equal(NotificationSendState.Accepted, feedback.SendState);
    }

    [Fact]
    public async Task Cancel_calls_off_pending_feedback_and_tells_shopper()
    {
        using var ctx = NewContext();
        var sms = new FakeSmsSender();
        var order = await SeedOrderAsync(ctx);
        await RegisterNumberAsync(ctx, "+15551110000");
        var service = NewService(ctx, sms);

        await service.NotifyOrderDispatchedAsync(order, default);
        var feedbackSid = (await NotificationsAsync(ctx, order.Id))
            .Single(n => n.Type == NotificationType.DeliveryFeedback).ProviderMessageSid;

        await service.NotifyOrderCancelledAsync(order, default);

        Assert.Contains(feedbackSid!, sms.Canceled);   // the follow-up was called off
        var notifications = await NotificationsAsync(ctx, order.Id);
        Assert.Equal(NotificationSendState.Canceled,
            notifications.Single(n => n.Type == NotificationType.DeliveryFeedback).SendState);
        Assert.Contains(notifications, n => n.Type == NotificationType.OrderCancelled);
    }

    [Fact]
    public async Task Resend_is_idempotent_under_the_same_key_and_genuine_under_a_fresh_key()
    {
        using var ctx = NewContext();
        var sms = new FakeSmsSender();
        var order = await SeedOrderAsync(ctx);
        await RegisterNumberAsync(ctx, "+15551110000");
        var service = NewService(ctx, sms);
        await service.NotifyOrderPlacedAsync(order, default);
        var original = (await NotificationsAsync(ctx, order.Id)).Single();
        var sentAfterPlace = sms.Sent.Count;

        var first = await service.ResendAsync(original.Id, "key-1", default);
        Assert.Equal(sentAfterPlace + 1, sms.Sent.Count);

        var repeat = await service.ResendAsync(original.Id, "key-1", default);
        Assert.Equal(first.Id, repeat.Id);                      // same message returned
        Assert.Equal(sentAfterPlace + 1, sms.Sent.Count);       // NOT sent again

        var fresh = await service.ResendAsync(original.Id, "key-2", default);
        Assert.NotEqual(first.Id, fresh.Id);
        Assert.Equal(sentAfterPlace + 2, sms.Sent.Count);       // a genuine second attempt
    }

    [Fact]
    public async Task Dispose_content_redacts_at_the_provider_and_locally()
    {
        using var ctx = NewContext();
        var sms = new FakeSmsSender();
        var order = await SeedOrderAsync(ctx);
        await RegisterNumberAsync(ctx, "+15551110000");
        var service = NewService(ctx, sms);
        await service.NotifyOrderPlacedAsync(order, default);
        var notification = (await NotificationsAsync(ctx, order.Id)).Single();

        await service.DisposeContentAsync(notification.Id, default);

        Assert.Contains(notification.ProviderMessageSid!, sms.Redacted);
        var refreshed = (await NotificationsAsync(ctx, order.Id)).Single();
        Assert.True(refreshed.ContentRedacted);
        Assert.Null(refreshed.Body);
    }

    [Fact]
    public async Task Reconcile_matches_by_sid_and_flags_both_one_sided_cases()
    {
        using var ctx = NewContext();
        var sms = new FakeSmsSender();
        var order = await SeedOrderAsync(ctx);
        await RegisterNumberAsync(ctx, "+15551110000");
        var service = NewService(ctx, sms);
        await service.NotifyOrderPlacedAsync(order, default);
        var eshopSid = (await NotificationsAsync(ctx, order.Id)).Single().ProviderMessageSid!;

        // Provider knows about the eShop message plus one eShop has no record of.
        sms.ProviderMessages.Add(new ProviderMessageRecord { Sid = eshopSid, Status = "delivered", From = sms.SendingNumber });
        sms.ProviderMessages.Add(new ProviderMessageRecord { Sid = "SM-provider-only", Status = "sent", From = sms.SendingNumber });

        var report = await service.ReconcileAsync(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), default);

        Assert.Equal(1, report.MatchedCount);
        Assert.Contains(report.Matched, e => e.Sid == eshopSid);
        Assert.Contains(report.ProviderOnly, e => e.Sid == "SM-provider-only");
        Assert.Equal(sms.SendingNumber, report.FromNumber);
    }

    [Fact]
    public async Task GetOrderNotifications_refreshes_delivery_state_from_the_provider()
    {
        using var ctx = NewContext();
        var sms = new FakeSmsSender { OnFetchStatus = _ => new SmsMessageStatus { Status = "delivered" } };
        var order = await SeedOrderAsync(ctx);
        await RegisterNumberAsync(ctx, "+15551110000");
        var service = NewService(ctx, sms);
        await service.NotifyOrderPlacedAsync(order, default);

        var refreshed = await service.GetOrderNotificationsAsync(order.Id, default);

        Assert.Single(refreshed);
        Assert.Equal(NotificationSendState.Delivered, refreshed[0].SendState);
        Assert.Equal("delivered", refreshed[0].ProviderStatus);
    }
}
