using System.Collections.Generic;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public abstract class PaymentServiceTestBase
{
    protected const string BuyerId = "demouser@microsoft.com";
    protected const int OrderId = 42;

    protected readonly IRepository<Order> OrderRepository = Substitute.For<IRepository<Order>>();
    protected readonly IRepository<Payment> PaymentRepository = Substitute.For<IRepository<Payment>>();
    protected readonly IRepository<CatalogItem> ItemRepository = Substitute.For<IRepository<CatalogItem>>();
    protected readonly IRepository<SavedCard> SavedCardRepository = Substitute.For<IRepository<SavedCard>>();
    protected readonly IPaymentGateway Gateway = Substitute.For<IPaymentGateway>();
    protected readonly IAppLogger<PaymentService> Logger = Substitute.For<IAppLogger<PaymentService>>();
    protected readonly PayPalSettings Settings = new() { Currency = "USD" };

    protected PaymentService CreateService() => new(
        OrderRepository, PaymentRepository, ItemRepository, SavedCardRepository, Gateway, Settings, Logger);

    protected static Order NewOrder(decimal unitPrice = 10m, int units = 2)
    {
        var address = new Address("123 Main St", "Kent", "OH", "USA", "44240");
        var items = new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Roslyn Red Sheet", "pic.png"), unitPrice, units)
        };
        return new Order(BuyerId, address, items);
    }

    protected static Payment NewAuthorizedPayment(int orderId = OrderId, decimal amount = 20m)
    {
        var payment = new Payment(orderId, BuyerId, amount, "USD");
        payment.MarkAuthorized("PAYPAL-ORDER-1", "AUTH-1", "CREATED", System.DateTimeOffset.UtcNow.AddDays(3));
        return payment;
    }

    protected void GivenOrder(Order order)
    {
        OrderRepository
            .FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(order);
    }

    protected void GivenPayment(Payment? payment)
    {
        PaymentRepository
            .FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<Payment>>(), Arg.Any<CancellationToken>())
            .Returns(payment);
    }
}
