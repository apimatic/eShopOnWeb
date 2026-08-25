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

public class RefundOrderAsync
{
    private readonly IRepository<Order> _orderRepo = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _catalogRepo = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<PaymentMethod> _paymentMethodRepo = Substitute.For<IRepository<PaymentMethod>>();
    private readonly IPayPalGateway _payPal = Substitute.For<IPayPalGateway>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly PayPalOptions _options = new() { Currency = "USD", Environment = "sandbox" };

    private OrderPaymentService CreateSut() => new(_orderRepo, _catalogRepo, _paymentMethodRepo, _payPal, _uriComposer, _options);

    private Order FulfilledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.BeginAuthorization(order.Total(), "USD", "PP-ORDER-1", "req-1", null, "AUTH-1", "CREATED", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3));
        order.MarkFulfilled("CAP-1", "COMPLETED", order.Total(), 0m, order.Total(), "cap-req", DateTimeOffset.UtcNow);
        _orderRepo.FirstOrDefaultAsync(Arg.Any<OrderByIdWithPaymentSpecification>(), default).Returns(order);
        return order;
    }

    [Fact]
    public async Task FullRefundCompletesAndUpdatesOrder()
    {
        var order = FulfilledOrder();
        _payPal.RefundCaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), default)
            .Returns(new PayPalRefundResult { RefundId = "REF-1", Status = "COMPLETED", Amount = order.Total(), CurrencyCode = "USD", CreateTime = DateTimeOffset.UtcNow });

        var sut = CreateSut();
        var refund = await sut.RefundOrderAsync(order.BuyerId, order.Id, null, "idem-1");

        Assert.Equal("REF-1", refund.PayPalRefundId);
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }

    [Fact]
    public async Task ExceedingRemainingRefundableThrows()
    {
        var order = FulfilledOrder();
        var sut = CreateSut();

        await Assert.ThrowsAsync<RefundAmountExceededException>(() =>
            sut.RefundOrderAsync(order.BuyerId, order.Id, order.Total() + 1m, "idem-1"));
    }

    [Fact]
    public async Task RepeatingSameIdempotencyKeyReturnsOriginalRefundWithoutSecondPayPalCall()
    {
        var order = FulfilledOrder();
        _payPal.RefundCaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), default)
            .Returns(new PayPalRefundResult { RefundId = "REF-1", Status = "COMPLETED", Amount = 1m, CurrencyCode = "USD", CreateTime = DateTimeOffset.UtcNow });

        var sut = CreateSut();
        var first = await sut.RefundOrderAsync(order.BuyerId, order.Id, 1m, "idem-1");
        var second = await sut.RefundOrderAsync(order.BuyerId, order.Id, 1m, "idem-1");

        Assert.Equal(first.Id, second.Id);
        await _payPal.Received(1).RefundCaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), default);
    }

    [Fact]
    public async Task ReusingIdempotencyKeyWithDifferentAmountThrowsConflict()
    {
        var order = FulfilledOrder();
        _payPal.RefundCaptureAsync(Arg.Any<string>(), 1m, Arg.Any<string>(), Arg.Any<string>(), default)
            .Returns(new PayPalRefundResult { RefundId = "REF-1", Status = "COMPLETED", Amount = 1m, CurrencyCode = "USD", CreateTime = DateTimeOffset.UtcNow });

        var sut = CreateSut();
        await sut.RefundOrderAsync(order.BuyerId, order.Id, 1m, "idem-1");

        await Assert.ThrowsAsync<IdempotencyConflictException>(() => sut.RefundOrderAsync(order.BuyerId, order.Id, 2m, "idem-1"));
    }

    [Fact]
    public async Task WrongBuyerThrowsForbidden()
    {
        var order = FulfilledOrder();
        var sut = CreateSut();

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => sut.RefundOrderAsync("someone-else", order.Id, null, "idem-1"));
    }
}
