using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents.Handlers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Seam;

/// <summary>
/// UC2's automatic trigger — one order placed bills one metered unit — and the guarantee that it
/// can never affect eShopOnWeb's own order lifecycle.
/// </summary>
public class OrderUsageHookTests
{
    private const string BuyerId = "shopper@example.com";

    private readonly ISubscriptionService _subscriptionService = Substitute.For<ISubscriptionService>();
    private readonly IAppLogger<RecordOrderUsageHandler> _logger =
        Substitute.For<IAppLogger<RecordOrderUsageHandler>>();

    private readonly RecordOrderUsageHandler _handler;

    public OrderUsageHookTests()
    {
        _handler = new RecordOrderUsageHandler(_subscriptionService, _logger);
    }

    private static OrderPlaced AnOrderPlaced() =>
        new OrderPlaced(new Order(BuyerId,
            new Address("123 Main St.", "Kent", "OH", "United States", "44240"),
            new List<OrderItem>
            {
                new OrderItem(new CatalogItemOrdered(1, "Item", "uri"), 10m, 1)
            }));

    [Fact]
    public async Task BillsExactlyOneUnitPerOrder()
    {
        _subscriptionService.RecordUsageAsync(BuyerId, 1m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UsageRecord(1, 10, "api-call", 1m) { PeriodToDateTotal = 41m });

        await _handler.Handle(AnOrderPlaced(), CancellationToken.None);

        await _subscriptionService.Received(1)
            .RecordUsageAsync(BuyerId, 1m, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShoppersWithoutASubscriptionAreSimplySkipped()
    {
        _subscriptionService.RecordUsageAsync(BuyerId, 1m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new NoActiveSubscriptionException(BuyerId));

        // The ordinary case for most shoppers — it must not surface as an error.
        await _handler.Handle(AnOrderPlaced(), CancellationToken.None);
    }

    [Fact]
    public async Task ASubscriptionThatIsNotLiveIsSkipped()
    {
        _subscriptionService.RecordUsageAsync(BuyerId, 1m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidSubscriptionTransitionException(10, SubscriptionStatus.OnHold,
                "record usage against", "active"));

        await _handler.Handle(AnOrderPlaced(), CancellationToken.None);
    }

    [Fact]
    public async Task AnUnreachableBillingProviderNeverFailsTheOrder()
    {
        _subscriptionService.RecordUsageAsync(BuyerId, 1m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("Maxio is down"));

        await _handler.Handle(AnOrderPlaced(), CancellationToken.None);

        _logger.ReceivedWithAnyArgs().LogWarning(default!);
    }

    [Fact]
    public async Task EvenAnUnexpectedFailureNeverFailsTheOrder()
    {
        _subscriptionService.RecordUsageAsync(BuyerId, 1m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("something nobody predicted"));

        await _handler.Handle(AnOrderPlaced(), CancellationToken.None);
    }
}

/// <summary>
/// The hook itself: placing an order announces it, and announcing it can never break checkout.
/// </summary>
public class OrderServicePublishesOrderPlacedTests
{
    private const string BuyerId = "shopper@example.com";

    private readonly IRepository<Basket> _basketRepository = Substitute.For<IRepository<Basket>>();
    private readonly IRepository<CatalogItem> _itemRepository = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<Order> _orderRepository = Substitute.For<IRepository<Order>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<OrderService> _logger = Substitute.For<IAppLogger<OrderService>>();

    private readonly OrderService _orderService;

    public OrderServicePublishesOrderPlacedTests()
    {
        const int catalogItemId = 1;

        var basket = new Basket(BuyerId);
        basket.AddItem(catalogItemId, 10m, 2);

        var catalogItem = new CatalogItem(1, 1, "An item", "An item", 10m, "uri");
        AssignIdentity(catalogItem, catalogItemId);

        _basketRepository.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Basket>>(),
                Arg.Any<CancellationToken>())
            .Returns(basket);

        _itemRepository.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<CatalogItem>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { catalogItem });

        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("uri");

        _orderService = new OrderService(_basketRepository, _itemRepository, _orderRepository,
            _uriComposer, _publisher, _logger);
    }

    private static Address AnAddress() =>
        new Address("123 Main St.", "Kent", "OH", "United States", "44240");

    /// <summary>
    /// Entity ids are assigned by the database, so a test double has to set one the same way EF
    /// would — through the protected setter on <see cref="BaseEntity"/>.
    /// </summary>
    private static void AssignIdentity(BaseEntity entity, int id) =>
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(entity, new object[] { id });

    [Fact]
    public async Task PlacingAnOrderAnnouncesItInProcess()
    {
        await _orderService.CreateOrderAsync(1, AnAddress());

        await _publisher.Received(1).Publish(
            Arg.Is<OrderPlaced>(notification => notification.BuyerId == BuyerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOrderIsPersistedBeforeItIsAnnounced()
    {
        await _orderService.CreateOrderAsync(1, AnAddress());

        Received.InOrder(() =>
        {
            _orderRepository.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
            _publisher.Publish(Arg.Any<OrderPlaced>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task AFailingListenerNeverFailsCheckout()
    {
        _publisher.Publish(Arg.Any<OrderPlaced>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BillingProviderException("Maxio is down"));

        // The order is already saved; checkout must still succeed.
        await _orderService.CreateOrderAsync(1, AnAddress());

        await _orderRepository.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }
}
