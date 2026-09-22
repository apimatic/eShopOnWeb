using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

/// <summary>
/// Exercises the notification orchestration against a real EF in-memory store. Each service call
/// gets a FRESH context over the SAME in-memory database — mirroring the app's scoped-context /
/// shared-store lifetime — so the resend idempotency claim is tested against the store's actual
/// duplicate-primary-key rejection at SaveChanges, not a fake. A counting fake SMS provider stands
/// in for Twilio.
/// </summary>
public class OrderNotificationServiceTests
{
    private const string Buyer = "buyer@example.com";

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FakeSmsProvider _sms = new();

    public OrderNotificationServiceTests()
    {
        using var seed = NewContext();
        var item = new CatalogItem(2, 1, "desc", "Test Item", 9.99m, "pic.png");
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(item, 1);
        seed.CatalogItems.Add(item);
        seed.SaveChanges();
    }

    private CatalogContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogContext>().UseInMemoryDatabase(_dbName).Options);

    // A fresh service over a fresh context each call — like a scoped request against the shared store.
    private OrderNotificationService NewService()
    {
        var context = NewContext();
        var uriComposer = Substitute.For<IUriComposer>();
        uriComposer.ComposePicUri(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return new OrderNotificationService(
            new EfRepository<Order>(context),
            new EfRepository<CatalogItem>(context),
            new EfRepository<ContactNumber>(context),
            new EfRepository<SmsNotification>(context),
            new ResendIdempotencyStore(context),
            _sms,
            uriComposer,
            Substitute.For<IAppLogger<OrderNotificationService>>());
    }

    private async Task RegisterNumberAsync(string number = "+15145550123")
    {
        using var context = NewContext();
        await new EfRepository<ContactNumber>(context).AddAsync(new ContactNumber(Buyer, number));
    }

    private async Task<int> CountNotificationsAsync()
    {
        using var context = NewContext();
        return await context.SmsNotifications.CountAsync();
    }

    [Fact]
    public async Task PlaceOrder_WithNoNumberOnFile_SendsNothing()
    {
        var orderId = await NewService().PlaceOrderAsync(Buyer, new List<OrderLineItem> { new(1, 1) }, AnAddress(), CancellationToken.None);

        Assert.True(orderId > 0);
        Assert.Equal(0, _sms.SendCount);
        Assert.Equal(0, await CountNotificationsAsync());
    }

    [Fact]
    public async Task PlaceOrder_WithNumberOnFile_SendsOrderPlaced()
    {
        await RegisterNumberAsync();
        var orderId = await NewService().PlaceOrderAsync(Buyer, new List<OrderLineItem> { new(1, 2) }, AnAddress(), CancellationToken.None);

        Assert.Equal(1, _sms.SendCount);
        using var context = NewContext();
        var notification = await context.SmsNotifications.SingleAsync();
        Assert.Equal(NotificationKind.OrderPlaced, notification.Kind);
        Assert.Equal(NotificationState.Sent, notification.State);
        Assert.Equal(orderId, notification.OrderId);
        Assert.NotNull(notification.ProviderSid);
    }

    [Fact]
    public async Task Dispatch_IsIdempotent_SecondCallIsNoOpAndSendsNothingExtra()
    {
        await RegisterNumberAsync();
        var orderId = await NewService().PlaceOrderAsync(Buyer, new List<OrderLineItem> { new(1, 1) }, AnAddress(), CancellationToken.None);
        var sendsAfterPlace = _sms.SendCount;

        var first = await NewService().DispatchOrderAsync(orderId, CancellationToken.None);
        var sendsAfterFirstDispatch = _sms.SendCount;
        var second = await NewService().DispatchOrderAsync(orderId, CancellationToken.None);

        Assert.Equal(OrderActionOutcome.Applied, first);
        Assert.Equal(OrderActionOutcome.NoChange, second);
        Assert.True(sendsAfterFirstDispatch > sendsAfterPlace);
        Assert.Equal(sendsAfterFirstDispatch, _sms.SendCount);
    }

    [Fact]
    public async Task Dispatch_QueuesFollowUp_AndCancelCallsItOff()
    {
        await RegisterNumberAsync();
        var orderId = await NewService().PlaceOrderAsync(Buyer, new List<OrderLineItem> { new(1, 1) }, AnAddress(), CancellationToken.None);

        await NewService().DispatchOrderAsync(orderId, CancellationToken.None);
        int followUpId;
        using (var context = NewContext())
        {
            var followUp = await context.SmsNotifications.SingleAsync(n => n.Kind == NotificationKind.DeliveryFollowUp);
            Assert.Equal(NotificationState.Scheduled, followUp.State);
            followUpId = followUp.Id;
        }
        Assert.Equal(1, _sms.ScheduleCount);

        await NewService().CancelOrderAsync(orderId, CancellationToken.None);

        using (var context = NewContext())
        {
            var afterCancel = await context.SmsNotifications.FindAsync(followUpId);
            Assert.Equal(NotificationState.Cancelled, afterCancel!.State);
        }
        Assert.Equal(1, _sms.CancelScheduledCount);
    }

    [Fact]
    public async Task Resend_RepeatUnderSameKey_SendsNoSecondMessage_ButFreshKeyDoes()
    {
        await RegisterNumberAsync();
        await NewService().PlaceOrderAsync(Buyer, new List<OrderLineItem> { new(1, 1) }, AnAddress(), CancellationToken.None);
        int originalId;
        using (var context = NewContext())
        {
            originalId = (await context.SmsNotifications.FirstAsync()).Id;
        }
        var sendsBefore = _sms.SendCount;

        var first = await NewService().ResendAsync(originalId, "key-1", CancellationToken.None);
        var sendsAfterFirst = _sms.SendCount;

        var repeat = await NewService().ResendAsync(originalId, "key-1", CancellationToken.None);
        var sendsAfterRepeat = _sms.SendCount;

        var fresh = await NewService().ResendAsync(originalId, "key-2", CancellationToken.None);
        var sendsAfterFresh = _sms.SendCount;

        Assert.NotNull(first);
        Assert.Equal(sendsBefore + 1, sendsAfterFirst);   // first resend sent once
        Assert.Equal(first, repeat);                      // same key replays the same notification id
        Assert.Equal(sendsAfterFirst, sendsAfterRepeat);  // ...and sent NO second message
        Assert.NotEqual(first, fresh);                    // a fresh key is a new, legitimate send
        Assert.Equal(sendsAfterRepeat + 1, sendsAfterFresh);
    }

    private static Address AnAddress() => new("1 Microsoft Way", "Redmond", "WA", "USA", "98052");

    private sealed class FakeSmsProvider : ISmsProvider
    {
        public int SendCount { get; private set; }
        public int ScheduleCount { get; private set; }
        public int CancelScheduledCount { get; private set; }

        public Task<PhoneValidationResult> ValidateAsync(string rawNumber, CancellationToken ct) =>
            Task.FromResult(new PhoneValidationResult(true, rawNumber));

        public Task<SmsSendResult> SendAsync(string to, string body, CancellationToken ct)
        {
            SendCount++;
            return Task.FromResult(new SmsSendResult
            {
                Outcome = SmsSendOutcome.Accepted,
                ProviderSid = "SM" + Guid.NewGuid().ToString("N"),
                Status = "queued",
                DateSent = DateTimeOffset.UtcNow
            });
        }

        public Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct)
        {
            ScheduleCount++;
            return Task.FromResult(new SmsSendResult
            {
                Outcome = SmsSendOutcome.Accepted,
                ProviderSid = "SM" + Guid.NewGuid().ToString("N"),
                Status = "scheduled"
            });
        }

        public Task<SmsSendResult> CancelScheduledAsync(string providerSid, CancellationToken ct)
        {
            CancelScheduledCount++;
            return Task.FromResult(new SmsSendResult { Outcome = SmsSendOutcome.Accepted, ProviderSid = providerSid, Status = "canceled" });
        }

        public Task<ProviderMessage?> FetchAsync(string providerSid, CancellationToken ct) =>
            Task.FromResult<ProviderMessage?>(null);

        public Task RedactContentAsync(string providerSid, CancellationToken ct) => Task.CompletedTask;

        public Task<ProviderMessageListResult> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult(new ProviderMessageListResult());

        public Task<ProviderMessage?> FindRecentAsync(string to, DateTimeOffset sentAfter, CancellationToken ct) =>
            Task.FromResult<ProviderMessage?>(null);
    }
}
