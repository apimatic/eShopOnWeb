using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public abstract class OrderPaymentServiceTestBase
{
    protected const string BuyerId = "test-buyer";

    protected readonly IRepository<Order> Orders = Substitute.For<IRepository<Order>>();
    protected readonly IRepository<CatalogItem> Items = Substitute.For<IRepository<CatalogItem>>();
    protected readonly IRepository<SavedCard> Cards = Substitute.For<IRepository<SavedCard>>();
    protected readonly IPaymentGateway Gateway = Substitute.For<IPaymentGateway>();
    protected readonly IUriComposer UriComposer = Substitute.For<IUriComposer>();
    protected readonly IAppLogger<OrderPaymentService> Logger = Substitute.For<IAppLogger<OrderPaymentService>>();

    protected OrderPaymentService CreateService()
    {
        return new OrderPaymentService(Orders, Items, Cards, Gateway, UriComposer,
            new PayPalSettings { Currency = "USD" }, Logger);
    }

    protected static Order NewOrder(int id = 1, string buyerId = BuyerId)
    {
        var item = new OrderItem(new CatalogItemOrdered(7, "Thing", "http://example.com/p.png"), 12m, 2);
        var order = new Order(buyerId, new Address("s", "c", "st", "co", "z"), new List<OrderItem> { item });
        order.SetCurrency("USD");
        SetId(order, id);
        return order;
    }

    protected static Order NewAuthorizedOrder(int id = 1, string buyerId = BuyerId,
        DateTimeOffset? expiresAt = null)
    {
        var order = NewOrder(id, buyerId);
        order.RegisterPayPalOrder("PP-ORDER-1");
        order.MarkAuthorized("AUTH-1", "CREATED", expiresAt ?? DateTimeOffset.UtcNow.AddDays(3));
        return order;
    }

    protected static Order NewCapturedOrder(int id = 1, decimal gross = 24m)
    {
        var order = NewAuthorizedOrder(id);
        order.MarkCaptured("CAP-1", gross, 1.11m, gross - 1.11m);
        return order;
    }

    protected static void SetAuthorizedAt(Order order, DateTimeOffset value)
    {
        typeof(Order).GetProperty(nameof(Order.AuthorizedAt))!.SetValue(order, value);
    }

    private static void SetId(BaseEntity entity, int id)
    {
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(entity, id);
    }

    protected void ReturnsOrder(Order? order)
    {
        Orders.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(order);
    }
}
