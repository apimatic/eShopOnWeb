using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities;
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

public class AuthorizePayment
{
    private const string BuyerId = "buyer@test.com";
    private const int OrderId = 1;

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _paymentRepo = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<CatalogItem> _catalogRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<Buyer> _buyerRepo = Substitute.For<IRepository<Buyer>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();

    private OrderPaymentService CreateService() =>
        new(_orderRepo, _paymentRepo, _catalogRepo, _buyerRepo, _gateway, "USD");

    private static Order NewOrder(string buyerId = BuyerId)
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Test", "pic.png"), 10m, 2);
        return new Order(buyerId, new AddressBuilder().WithDefaultValues(), new List<OrderItem> { item });
    }

    [Fact]
    public async Task AuthorizesWithCardAndUpdatesPaymentAndOrder()
    {
        var order = NewOrder();
        var payment = new Payment(OrderId, order.Total(), "USD");

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);
        _gateway.AuthorizeWithCardAsync(Arg.Any<CardDetails>(), payment.Amount, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CardAuthorizationResult("pp-order-1", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));

        var service = CreateService();
        var card = new CardDetails("John Doe", "4111111111111111", "2028-12", "123", "US");

        var result = await service.AuthorizePaymentAsync(BuyerId, OrderId, card, null, CancellationToken.None);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        Assert.Equal("auth-1", result.PayPalAuthorizationId);
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        await _paymentRepo.Received().UpdateAsync(payment, Arg.Any<CancellationToken>());
        await _orderRepo.Received().UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizesWithSavedCard()
    {
        var order = NewOrder();
        var payment = new Payment(OrderId, order.Total(), "USD");
        var buyer = new Buyer(BuyerId);
        var savedCard = buyer.AddPaymentMethod("vault-1", "VISA", "1111", "2028-12");

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);
        _buyerRepo.FirstOrDefaultAsync(Arg.Any<BuyerWithPaymentMethodsSpecification>(), Arg.Any<CancellationToken>()).Returns(buyer);
        _gateway.AuthorizeWithVaultedCardAsync("vault-1", payment.Amount, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CardAuthorizationResult("pp-order-2", "auth-2", "CREATED", DateTimeOffset.UtcNow.AddDays(3)));

        var service = CreateService();

        var result = await service.AuthorizePaymentAsync(BuyerId, OrderId, null, savedCard.Id, CancellationToken.None);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        Assert.Equal(savedCard.Id, result.PaymentMethodId);
    }

    [Fact]
    public async Task IsIdempotentWhenAlreadyAuthorized()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        var payment = new Payment(OrderId, order.Total(), "USD");
        payment.MarkAuthorized("pp-order-1", "auth-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), null);

        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);

        var service = CreateService();
        var card = new CardDetails("John Doe", "4111111111111111", "2028-12", "123", "US");

        var result = await service.AuthorizePaymentAsync(BuyerId, OrderId, card, null, CancellationToken.None);

        Assert.Same(payment, result);
        await _gateway.DidNotReceive().AuthorizeWithCardAsync(Arg.Any<CardDetails>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsWhenOrderDoesNotBelongToBuyer()
    {
        var order = NewOrder("someone-else@test.com");
        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);

        var service = CreateService();
        var card = new CardDetails("John Doe", "4111111111111111", "2028-12", "123", "US");

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.AuthorizePaymentAsync(BuyerId, OrderId, card, null, CancellationToken.None));
    }

    [Fact]
    public async Task ThrowsWhenNeitherCardNorSavedMethodProvided()
    {
        var order = NewOrder();
        var payment = new Payment(OrderId, order.Total(), "USD");
        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => service.AuthorizePaymentAsync(BuyerId, OrderId, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task ThrowsWhenBothCardAndSavedMethodProvided()
    {
        var order = NewOrder();
        var payment = new Payment(OrderId, order.Total(), "USD");
        _orderRepo.GetByIdAsync(OrderId, Arg.Any<CancellationToken>()).Returns(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>()).Returns(payment);

        var service = CreateService();
        var card = new CardDetails("John Doe", "4111111111111111", "2028-12", "123", "US");

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => service.AuthorizePaymentAsync(BuyerId, OrderId, card, 1, CancellationToken.None));
    }
}
