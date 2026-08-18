using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
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

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

public class OrderNotificationServiceTests
{
    private const string BuyerA = "buyerA@test.com";
    private const string BuyerB = "buyerB@test.com";

    private readonly CatalogContext _context;
    private readonly EfRepository<ContactNumber> _contactNumbers;
    private readonly EfRepository<OrderNotification> _notifications;
    private readonly EfRepository<Order> _orders;
    private readonly EfRepository<CatalogItem> _catalogItems;
    private readonly FakeSmsGateway _gateway = new();
    private readonly OrderNotificationService _service;

    public OrderNotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase("notif-tests-" + Guid.NewGuid())
            .Options;
        _context = new CatalogContext(options);
        _contactNumbers = new EfRepository<ContactNumber>(_context);
        _notifications = new EfRepository<OrderNotification>(_context);
        _orders = new EfRepository<Order>(_context);
        _catalogItems = new EfRepository<CatalogItem>(_context);
        _service = new OrderNotificationService(
            _contactNumbers, _notifications, _orders, _catalogItems,
            _gateway, new PassthroughUriComposer(),
            Substitute.For<IAppLogger<OrderNotificationService>>());
    }

    private async Task<int> SeedCatalogItemAsync(decimal price = 9.99m)
    {
        var item = await _catalogItems.AddAsync(new CatalogItem(1, 1, "desc", "Widget", price, "widget.png"));
        return item.Id;
    }

    [Fact]
    public async Task Register_StoresProviderCanonicalForm_NotRawInput()
    {
        _gateway.CanonicalNumber = "+15145551234";

        var stored = await _service.RegisterContactNumberAsync(BuyerA, "(514) 555-1234");

        Assert.Equal("+15145551234", stored.PhoneNumber);
        var all = await _service.GetContactNumbersAsync(BuyerA);
        Assert.Single(all);
        Assert.Equal("+15145551234", all[0].PhoneNumber);
    }

    [Fact]
    public async Task Register_InvalidNumber_IsRejected_AndStoresNothing()
    {
        _gateway.ValidationSucceeds = false;

        await Assert.ThrowsAsync<InvalidPhoneNumberException>(
            () => _service.RegisterContactNumberAsync(BuyerA, "not-a-number"));

        Assert.Empty(await _service.GetContactNumbersAsync(BuyerA));
    }

    [Fact]
    public async Task RemoveContactNumber_IsScopedToOwner()
    {
        var mine = await _service.RegisterContactNumberAsync(BuyerA, "555");

        // Another shopper cannot delete it.
        Assert.False(await _service.RemoveContactNumberAsync(BuyerB, mine.Id));
        Assert.Single(await _service.GetContactNumbersAsync(BuyerA));

        // The owner can, and afterwards it is gone.
        Assert.True(await _service.RemoveContactNumberAsync(BuyerA, mine.Id));
        Assert.Empty(await _service.GetContactNumbersAsync(BuyerA));
    }

    [Fact]
    public async Task PlaceOrder_CreatesOrder_AndSendsPlacedNotification()
    {
        var itemId = await SeedCatalogItemAsync(5m);
        await _service.RegisterContactNumberAsync(BuyerA, "555");

        var order = await _service.PlaceOrderAsync(BuyerA, new[] { new OrderLineRequest(itemId, 2) });

        Assert.True(order.Id > 0);
        Assert.Equal(10m, order.Total());
        Assert.Single(_gateway.Sent);

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id));
        Assert.Single(notifications);
        Assert.Equal(NotificationKind.OrderPlaced, notifications[0].Kind);
        Assert.False(string.IsNullOrEmpty(notifications[0].MessageSid));
    }

    [Fact]
    public async Task PlaceOrder_SendFailure_DoesNotFailTheOrder()
    {
        var itemId = await SeedCatalogItemAsync();
        await _service.RegisterContactNumberAsync(BuyerA, "555");
        _gateway.ThrowOnSend = true;

        var order = await _service.PlaceOrderAsync(BuyerA, new[] { new OrderLineRequest(itemId, 1) });

        Assert.True(order.Id > 0); // order still placed
        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id));
        Assert.Single(notifications);
        Assert.Equal("not_sent", notifications[0].ProviderStatus);
        Assert.Null(notifications[0].MessageSid);
    }

    [Fact]
    public async Task PlaceOrder_WithNoNumberOnFile_IsSimplyNotMessaged()
    {
        var itemId = await SeedCatalogItemAsync();

        var order = await _service.PlaceOrderAsync(BuyerA, new[] { new OrderLineRequest(itemId, 1) });

        Assert.True(order.Id > 0);
        Assert.Empty(_gateway.Sent);
        Assert.Empty(await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id)));
    }

    [Fact]
    public async Task Dispatch_QueuesFollowUp_AndCancel_CallsItOff()
    {
        var itemId = await SeedCatalogItemAsync();
        await _service.RegisterContactNumberAsync(BuyerA, "555");
        var order = await _service.PlaceOrderAsync(BuyerA, new[] { new OrderLineRequest(itemId, 1) });

        Assert.True(await _service.DispatchOrderAsync(order.Id));
        Assert.Single(_gateway.Scheduled); // follow-up queued with the provider

        var followUp = (await _notifications.ListAsync(new ScheduledFollowUpsForOrderSpecification(order.Id))).Single();
        Assert.True(followUp.IsScheduled);
        var followUpSid = followUp.MessageSid!;

        Assert.True(await _service.CancelOrderAsync(order.Id));

        Assert.Contains(followUpSid, _gateway.Canceled); // the follow-up was actually cancelled at the provider
        var refreshed = await _notifications.GetByIdAsync(followUp.Id);
        Assert.Equal("canceled", refreshed!.ProviderStatus);
        Assert.False(refreshed.IsScheduled);
    }

    [Fact]
    public async Task Resend_SameKey_DoesNotSendTwice_FreshKeyDoes()
    {
        var itemId = await SeedCatalogItemAsync();
        await _service.RegisterContactNumberAsync(BuyerA, "555");
        var order = await _service.PlaceOrderAsync(BuyerA, new[] { new OrderLineRequest(itemId, 1) });
        var original = (await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id))).Single();
        var sentAfterPlace = _gateway.Sent.Count; // 1

        var first = await _service.ResendAsync(original.Id, "key-1");
        Assert.NotNull(first);
        Assert.Equal(sentAfterPlace + 1, _gateway.Sent.Count);

        var replay = await _service.ResendAsync(original.Id, "key-1");
        Assert.Equal(first!.Id, replay!.Id);                 // same result
        Assert.Equal(sentAfterPlace + 1, _gateway.Sent.Count); // no second send

        var freshAttempt = await _service.ResendAsync(original.Id, "key-2");
        Assert.NotEqual(first.Id, freshAttempt!.Id);
        Assert.Equal(sentAfterPlace + 2, _gateway.Sent.Count); // a fresh key does send
    }

    [Fact]
    public async Task GetNotificationsForOrder_IsScopedToOwner()
    {
        var itemId = await SeedCatalogItemAsync();
        await _service.RegisterContactNumberAsync(BuyerA, "555");
        var order = await _service.PlaceOrderAsync(BuyerA, new[] { new OrderLineRequest(itemId, 1) });

        Assert.Null(await _service.GetNotificationsForOrderAsync(order.Id, BuyerB)); // not theirs
        var mine = await _service.GetNotificationsForOrderAsync(order.Id, BuyerA);
        Assert.NotNull(mine);
        Assert.Single(mine!);
    }

    [Fact]
    public async Task RedactContent_DisposesAtProvider_AndDropsLocalText()
    {
        var itemId = await SeedCatalogItemAsync();
        await _service.RegisterContactNumberAsync(BuyerA, "555");
        var order = await _service.PlaceOrderAsync(BuyerA, new[] { new OrderLineRequest(itemId, 1) });
        var notification = (await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id))).Single();

        Assert.True(await _service.RedactNotificationContentAsync(notification.Id));

        Assert.Contains(notification.MessageSid!, _gateway.Redacted); // provider redaction requested
        var refreshed = await _notifications.GetByIdAsync(notification.Id);
        Assert.True(refreshed!.ContentRedacted);
        Assert.Null(refreshed.Body); // local text dropped, metadata (sid/status) survives
        Assert.False(string.IsNullOrEmpty(refreshed.MessageSid));
    }

    [Fact]
    public async Task Reconcile_MatchesBySid_AndSurfacesEachSideOnly()
    {
        var itemId = await SeedCatalogItemAsync();
        await _service.RegisterContactNumberAsync(BuyerA, "555");
        var order = await _service.PlaceOrderAsync(BuyerA, new[] { new OrderLineRequest(itemId, 1) });
        var eshopSid = (await _notifications.ListAsync(new OrderNotificationsByOrderSpecification(order.Id))).Single().MessageSid!;

        // Provider knows the eShop message plus one eShop never recorded.
        _gateway.ProviderMessages.Add(new ProviderMessage(eshopSid, "delivered", DateTimeOffset.UtcNow, null));
        _gateway.ProviderMessages.Add(new ProviderMessage("SM-provider-only", "sent", DateTimeOffset.UtcNow, null));

        var report = await _service.ReconcileAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));

        Assert.Single(report.Matched);
        Assert.Equal(eshopSid, report.Matched[0].MessageSid);
        Assert.Single(report.OnlyInProvider);
        Assert.Equal("SM-provider-only", report.OnlyInProvider[0].MessageSid);
        Assert.Empty(report.OnlyInEShop);
    }

    private sealed class PassthroughUriComposer : IUriComposer
    {
        public string ComposePicUri(string uriTemplate) => uriTemplate;
    }
}
