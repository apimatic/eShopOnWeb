using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class PayAndRefundOrder
{
    private const string BuyerId = "buyer-1";

    private readonly IRepository<Order> _orderRepository = Substitute.For<IRepository<Order>>();
    private readonly IRepository<CatalogItem> _itemRepository = Substitute.For<IRepository<CatalogItem>>();
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository = Substitute.For<IRepository<SavedPaymentMethod>>();
    private readonly IUriComposer _uriComposer = Substitute.For<IUriComposer>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();

    private OrderPaymentService CreateService() =>
        new OrderPaymentService(_orderRepository, _itemRepository, _paymentMethodRepository, _uriComposer, _gateway);

    private static Order AwaitingOrder()
    {
        var items = new List<OrderItem> { new OrderItem(new CatalogItemOrdered(1, "Item", "pic.png"), 10m, 1) };
        return new Order(BuyerId, new Address("s", "c", "st", "US", "00000"), items);
    }

    private static Order PaidOrder()
    {
        var order = AwaitingOrder();
        order.MarkPaid("pp-order", "capture-1");
        return order;
    }

    private void SetupOrderLookup(Order? order) =>
        _orderRepository.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(order);

    private static PaymentInstruction WithCard() =>
        new PaymentInstruction { Card = new CardDetails { Number = "4111111111111111", Expiry = "2030-01", SecurityCode = "123" } };

    [Fact]
    public async Task PayWithCard_MarksOrderPaidAndPersists()
    {
        SetupOrderLookup(AwaitingOrder());
        _gateway.CaptureWithCardAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult { PayPalOrderId = "pp-order", CaptureId = "capture-1", Status = "COMPLETED" });

        var order = await CreateService().PayOrderAsync(BuyerId, 1, WithCard());

        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal("capture-1", order.PayPalCaptureId);
        await _orderRepository.Received(1).UpdateAsync(order, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayAlreadyPaidOrder_IsIdempotent_DoesNotChargeAgain()
    {
        SetupOrderLookup(PaidOrder());

        var order = await CreateService().PayOrderAsync(BuyerId, 1, WithCard());

        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        await _gateway.DidNotReceive().CaptureWithCardAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardDetails>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _orderRepository.DidNotReceive().UpdateAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayUnknownOrder_Throws_OrderNotFound()
    {
        SetupOrderLookup(null);

        await Assert.ThrowsAsync<OrderNotFoundException>(
            () => CreateService().PayOrderAsync(BuyerId, 99, WithCard()));
    }

    [Fact]
    public async Task PayWithSavedCardNotOwned_Throws_PaymentMethodNotFound()
    {
        SetupOrderLookup(AwaitingOrder());
        _paymentMethodRepository.FirstOrDefaultAsync(Arg.Any<ISpecification<SavedPaymentMethod>>(), Arg.Any<CancellationToken>())
            .Returns((SavedPaymentMethod?)null);

        var instruction = new PaymentInstruction { SavedPaymentMethodId = 5 };

        await Assert.ThrowsAsync<PaymentMethodNotFoundException>(
            () => CreateService().PayOrderAsync(BuyerId, 1, instruction));
    }

    [Fact]
    public async Task Pay_WithBothOrNeitherSource_Throws()
    {
        SetupOrderLookup(AwaitingOrder());

        await Assert.ThrowsAsync<System.ArgumentException>(
            () => CreateService().PayOrderAsync(BuyerId, 1, new PaymentInstruction()));
    }

    [Fact]
    public async Task RefundPaidOrder_MarksRefundedAndPersists()
    {
        SetupOrderLookup(PaidOrder());
        _gateway.RefundCaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RefundResult { RefundId = "refund-1", Status = "COMPLETED" });

        var order = await CreateService().RefundOrderAsync(BuyerId, 1);

        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal("refund-1", order.PayPalRefundId);
        await _gateway.Received(1).RefundCaptureAsync("capture-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundAlreadyRefundedOrder_IsIdempotent()
    {
        var order = PaidOrder();
        order.MarkRefunded("refund-1");
        SetupOrderLookup(order);

        var result = await CreateService().RefundOrderAsync(BuyerId, 1);

        Assert.Equal(PaymentStatus.Refunded, result.PaymentStatus);
        await _gateway.DidNotReceive().RefundCaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundUnpaidOrder_Throws_PaymentFailed()
    {
        SetupOrderLookup(AwaitingOrder());

        await Assert.ThrowsAsync<PaymentFailedException>(
            () => CreateService().RefundOrderAsync(BuyerId, 1));
    }
}
