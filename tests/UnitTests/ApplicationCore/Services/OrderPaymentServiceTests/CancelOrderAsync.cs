using System;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class CancelOrderAsync
{
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalogRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<PaymentMethod> _paymentMethodRepo = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalGateway _payPal = Substitute.For<IPayPalGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly PayPalOptions _options = new() { Currency = "USD", Environment = "sandbox" };

    private OrderPaymentService CreateSut() => new(_orderRepo, _catalogRepo, _paymentMethodRepo, _payPal, _uriComposer, _options);

    [Fact]
    public async Task BeforeAuthorizationJustMarksCancelledWithNoPayPalCall()
    {
        var order = new OrderBuilder().WithDefaultValues();
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpecification>(), default).Returns(order);

        var sut = CreateSut();
        var result = await sut.CancelOrderAsync(order.Id);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        await _payPal.DidNotReceiveWithAnyArgs().VoidAuthorizationAsync(default!, default);
    }

    [Fact]
    public async Task AfterAuthorizationVoidsHeldFunds()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null, "AUTH-1", "CREATED", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3));
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpecification>(), default).Returns(order);

        var sut = CreateSut();
        var result = await sut.CancelOrderAsync(order.Id);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        await _payPal.Received(1).VoidAuthorizationAsync("AUTH-1", default);
    }

    [Fact]
    public async Task AfterFulfilmentThrows()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null, "AUTH-1", "CREATED", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3));
        order.MarkFulfilled("CAP-1", "COMPLETED", order.Total(), 0m, order.Total(), "cap-req", DateTimeOffset.UtcNow);
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpecification>(), default).Returns(order);

        var sut = CreateSut();
        await Assert.ThrowsAsync<InvalidOrderStateException>(() => sut.CancelOrderAsync(order.Id));
    }
}
