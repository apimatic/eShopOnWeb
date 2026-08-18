using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Data;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Services;

public class OrderNotificationServiceTests
{
    private readonly CatalogContext _context;
    private readonly ISmsProvider _sms = Substitute.For<ISmsProvider>();
    private readonly EfRepository<OrderNotification> _notificationRepository;
    private readonly OrderNotificationService _service;

    public OrderNotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(databaseName: "OrderNotificationServiceTests-" + Guid.NewGuid())
            .Options;
        _context = new CatalogContext(options);
        _notificationRepository = new EfRepository<OrderNotification>(_context);

        var uriComposer = Substitute.For<IUriComposer>();
        uriComposer.ComposePicUri(Arg.Any<string>()).Returns("pic.png");

        _service = new OrderNotificationService(
            new EfRepository<Order>(_context),
            _notificationRepository,
            new EfRepository<ContactNumber>(_context),
            new EfRepository<CatalogItem>(_context),
            _sms,
            uriComposer,
            Substitute.For<IAppLogger<OrderNotificationService>>());
    }

    private async Task<int> SeedCatalogItemAsync()
    {
        var item = new CatalogItem(1, 1, "desc", "Test Item", 9.99m, "pic.png");
        _context.CatalogItems.Add(item);
        await _context.SaveChangesAsync();
        return item.Id;
    }

    private async Task SeedContactNumberAsync(string buyerId)
    {
        _context.ContactNumbers.Add(new ContactNumber(buyerId, "+18254751588"));
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task PlaceOrderSucceeds_EvenWhenMessagingFails()
    {
        var itemId = await SeedCatalogItemAsync();
        await SeedContactNumberAsync("buyer-1");

        // The provider cannot be reached — the order must still be placed.
        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SmsDispatchResult>(_ => throw new SmsProviderException("provider unreachable"));

        var order = await _service.PlaceOrderAsync(
            "buyer-1",
            new List<OrderLineRequest> { new(itemId, 1) },
            new Address("s", "c", "st", "co", "00000"));

        Assert.NotNull(order);
        Assert.True(order.Id > 0);

        var notifications = await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(order.Id));
        var placed = Assert.Single(notifications);
        Assert.Equal(NotificationKind.OrderPlaced, placed.Kind);
        Assert.Null(placed.ProviderMessageSid);            // nothing reached the provider
        Assert.False(string.IsNullOrEmpty(placed.FailureReason)); // but the attempt is recorded
    }

    [Fact]
    public async Task PlaceOrderWithNoNumberOnFile_SendsNothing()
    {
        var itemId = await SeedCatalogItemAsync();

        var order = await _service.PlaceOrderAsync(
            "buyer-without-number",
            new List<OrderLineRequest> { new(itemId, 1) },
            new Address("s", "c", "st", "co", "00000"));

        Assert.NotNull(order);
        await _sms.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(await _notificationRepository.ListAsync(new OrderNotificationsByOrderSpecification(order.Id)));
    }

    [Fact]
    public async Task Resend_IsIdempotentOnKey_AndSendsAgainOnFreshKey()
    {
        var original = new OrderNotification(42, "buyer-1", NotificationKind.OrderPlaced, "+18254751588", "hi");
        original.RecordProviderResult("SMoriginal", "undelivered", 30006, "unreachable");
        _context.OrderNotifications.Add(original);
        await _context.SaveChangesAsync();

        _sms.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsDispatchResult { MessageSid = "SMresent", Status = "queued" });

        var first = await _service.ResendAsync(original.Id, "key-1");
        var repeat = await _service.ResendAsync(original.Id, "key-1");
        var fresh = await _service.ResendAsync(original.Id, "key-2");

        Assert.NotNull(first);
        Assert.Equal(first!.Id, repeat!.Id);        // same notification returned for the repeated key
        Assert.NotEqual(first.Id, fresh!.Id);       // a fresh key is a genuine new attempt

        // Exactly two messages went out: key-1 once, key-2 once; the repeat sent nothing.
        await _sms.Received(2).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
