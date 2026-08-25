using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class PlaceOrder
{
    private const string BuyerId = "buyer@test.com";

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _paymentRepo = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<CatalogItem> _catalogRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<Buyer> _buyerRepo = Substitute.For<IRepository<Buyer>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();

    private OrderPaymentService CreateService() =>
        new(_orderRepo, _paymentRepo, _catalogRepo, _buyerRepo, _gateway, "USD");

    [Fact]
    public async Task CreatesOrderAndAwaitingPaymentAsAggregate()
    {
        var catalogItem = new CatalogItem(1, 1, "desc", "Mug", 8.5m, "pic.png");
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(catalogItem, 5);
        _catalogRepo.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem> { catalogItem });
        _orderRepo.AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var addedOrder = callInfo.Arg<Order>();
                typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(addedOrder, 7);
                return addedOrder;
            });

        var items = new List<OrderItemRequest> { new(catalogItem.Id, 2) };
        var order = await CreateService().PlaceOrderAsync(BuyerId, items, new AddressBuilder().WithDefaultValues(), CancellationToken.None);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(17m, order.Total());
        await _paymentRepo.Received(1).AddAsync(Arg.Is<Payment>(p => p.Amount == 17m && p.Currency == "USD"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsWhenCatalogItemDoesNotExist()
    {
        _catalogRepo.ListAsync(Arg.Any<CatalogItemsSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new List<CatalogItem>());

        var items = new List<OrderItemRequest> { new(99, 1) };

        await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => CreateService().PlaceOrderAsync(BuyerId, items, new AddressBuilder().WithDefaultValues(), CancellationToken.None));
    }

    [Fact]
    public async Task ThrowsWhenNoItemsProvided()
    {
        await Assert.ThrowsAsync<InvalidOrderStateException>(
            () => CreateService().PlaceOrderAsync(BuyerId, new List<OrderItemRequest>(), new AddressBuilder().WithDefaultValues(), CancellationToken.None));
    }
}
