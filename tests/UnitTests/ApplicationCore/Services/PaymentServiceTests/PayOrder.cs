using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class PayOrder
{
    private const string BuyerId = "buyer-1";
    private const int OrderId = 7;

    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<Payment> _paymentRepo = Substitute.For<IRepository<Payment>>();
    private readonly IRepository<SavedPaymentMethod> _savedMethodRepo = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly IAppLogger<PaymentService> _logger = Substitute.For<IAppLogger<PaymentService>>();

    private PaymentService CreateService() => new PaymentService(
        _orderRepo, _paymentRepo, _savedMethodRepo, _gateway,
        new PayPalOptions { Currency = "USD" }, _logger);

    private static Order CreateOrder(string buyerId = BuyerId)
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Widget", "http://img"), 12.50m, 2);
        return new OrderWithId(buyerId, new Address("Main st", "City", "ST", "Country", "12345"), new List<OrderItem> { item }, OrderId);
    }

    private void GivenOrder(Order order)
        => _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderWithItemsByIdSpec>(), Arg.Any<CancellationToken>()).Returns(order);

    [Fact]
    public async Task AuthorizesAndPersistsOnHappyPath()
    {
        var order = CreateOrder();
        GivenOrder(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpec>(), Arg.Any<CancellationToken>())
            .Returns((Payment?)null);
        _gateway.AuthorizeAsync(Arg.Any<AuthorizePaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO-1", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), order.Total(), "USD", null));

        var payment = await CreateService().PayAsync(BuyerId, OrderId, new CardDetails("4111111111111111", "2030-01", null, null, null), null, default);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("AUTH-1", payment.AuthorizationId);
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        await _paymentRepo.Received().UpdateAsync(payment, Arg.Any<CancellationToken>());
        await _orderRepo.Received().UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnsExistingAuthorizationOnReplayWithoutCallingGateway()
    {
        var order = CreateOrder();
        order.MarkPaymentAuthorized();
        GivenOrder(order);
        var existing = new Payment(OrderId, BuyerId, order.Total(), "USD");
        existing.MarkAuthorized("PPO-1", "AUTH-1", "CREATED", null);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var payment = await CreateService().PayAsync(BuyerId, OrderId, new CardDetails("4111111111111111", "2030-01", null, null, null), null, default);

        Assert.Same(existing, payment);
        await _gateway.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizePaymentRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThrowsNotFoundWhenOrderBelongsToAnotherBuyer()
    {
        GivenOrder(CreateOrder(buyerId: "someone-else"));

        await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => CreateService().PayAsync(BuyerId, OrderId, new CardDetails("4111111111111111", "2030-01", null, null, null), null, default));
    }

    [Fact]
    public async Task ThrowsNotFoundWhenSavedCardBelongsToAnotherBuyer()
    {
        GivenOrder(CreateOrder());
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpec>(), Arg.Any<CancellationToken>())
            .Returns((Payment?)null);
        _savedMethodRepo.FirstOrDefaultAsync(Arg.Any<SavedPaymentMethodByIdSpec>(), Arg.Any<CancellationToken>())
            .Returns(new SavedPaymentMethod("someone-else", "VAULT-1", null, "VISA", "1111", "2030-01"));

        await Assert.ThrowsAsync<ResourceNotFoundException>(
            () => CreateService().PayAsync(BuyerId, OrderId, null, 3, default));
    }

    [Fact]
    public async Task MarksDeclinedAndThrowsWhenGatewayDeclines()
    {
        var order = CreateOrder();
        GivenOrder(order);
        _paymentRepo.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpec>(), Arg.Any<CancellationToken>())
            .Returns((Payment?)null);
        _gateway.AuthorizeAsync(Arg.Any<AuthorizePaymentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PPO-1", string.Empty, "DECLINED", null, order.Total(), "USD", "insufficient funds"));

        await Assert.ThrowsAsync<PaymentDeclinedException>(
            () => CreateService().PayAsync(BuyerId, OrderId, new CardDetails("4111111111111111", "2030-01", null, null, null), null, default));

        await _paymentRepo.Received().UpdateAsync(
            Arg.Is<Payment>(p => p.Status == PaymentStatus.Declined), Arg.Any<CancellationToken>());
    }

    private class OrderWithId : Order
    {
        public OrderWithId(string buyerId, Address address, List<OrderItem> items, int id)
            : base(buyerId, address, items)
        {
            Id = id;
        }
    }
}
