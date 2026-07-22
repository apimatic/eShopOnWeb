using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BasketAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.IntegrationEventHandlerTests;

/// <summary>
/// The hook that connects eShopOnWeb's existing checkout to subscription usage metering
/// (plan.md §8, UC2): placing an order announces itself in-process so one billable unit can be
/// recorded. The order lifecycle must be completely unaffected by whether that succeeds.
/// </summary>
public class OrderPlacedPublication
{
    private const string BuyerId = "demouser@microsoft.com";
    private const int BasketId = 7;
    private const int CatalogItemId = 3;

    /// <summary>
    /// A catalog item with a known id. Entity ids are normally assigned by EF through a protected
    /// setter, so a derived double is the least invasive way to seed one in a unit test.
    /// </summary>
    private sealed class TestCatalogItem : CatalogItem
    {
        public TestCatalogItem(int id)
            : base(1, 1, "a description", "a product", 12.34m, "pic.png")
        {
            Id = id;
        }
    }

    private readonly IRepository<Basket> _basketRepository = Substitute.For<IRepository<Basket>>();
    private readonly IRepository<CatalogItem> _itemRepository = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<Order> _orderRepository = Substitute.For<IRepository<Order>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<OrderService> _logger = Substitute.For<IAppLogger<OrderService>>();

    public OrderPlacedPublication()
    {
        var basket = new Basket(BuyerId);
        basket.AddItem(CatalogItemId, 12.34m, 2);

        _basketRepository.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Basket>>(),
                Arg.Any<CancellationToken>())
            .Returns(basket);

        _itemRepository.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<CatalogItem>>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { new TestCatalogItem(CatalogItemId) });

        _uriComposer.ComposePicUri(Arg.Any<string>()).Returns("http://example.com/pic.png");
        _orderRepository.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Order>());
    }

    private OrderService Service => new(_basketRepository, _itemRepository, _orderRepository,
        _uriComposer, _publisher, _logger);

    private static Address AnAddress() => new("123 Main St.", "Kent", "OH", "United States", "44240");

    [Fact]
    public async Task AnnouncesTheOrderSoUsageCanBeMetered()
    {
        await Service.CreateOrderAsync(BasketId, AnAddress());

        await _publisher.Received(1).Publish(
            Arg.Is<OrderPlaced>(placed => placed.BuyerId == BuyerId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistsTheOrderBeforeAnnouncingIt()
    {
        await Service.CreateOrderAsync(BasketId, AnAddress());

        Received.InOrder(() =>
        {
            _orderRepository.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
            _publisher.Publish(Arg.Any<OrderPlaced>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task StillCreatesTheOrderWhenTheUsageHandlerThrows()
    {
        // This is the guarantee that matters: a billing failure inside a handler must never
        // propagate out of checkout, and must never roll an order back.
        _publisher.Publish(Arg.Any<OrderPlaced>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("billing provider exploded"));

        await Service.CreateOrderAsync(BasketId, AnAddress());

        await _orderRepository.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        _logger.ReceivedWithAnyArgs(1).LogWarning(default!);
    }

    [Fact]
    public async Task DoesNotAnnounceAnOrderThatWasNeverCreated()
    {
        _basketRepository.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Basket>>(),
                Arg.Any<CancellationToken>())
            .Returns((Basket?)null);

        await Assert.ThrowsAnyAsync<Exception>(() => Service.CreateOrderAsync(BasketId, AnAddress()));

        await _publisher.DidNotReceive().Publish(Arg.Any<OrderPlaced>(), Arg.Any<CancellationToken>());
    }
}
