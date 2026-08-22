using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    private static Order PaidOrder()
    {
        var builder = new OrderBuilder();
        var items = new List<OrderItem>
        {
            new OrderItem(builder.TestCatalogItemOrdered, 10.00m, 2)
        };
        return new OrderBuilder().WithItems(items);
    }

    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = PaidOrder();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(20.00m, order.Total());
    }

    [Fact]
    public void RecordsAuthorizationAndIsIdempotentForSameHold()
    {
        var order = PaidOrder();
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", 20.00m, "USD", null);
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", 20.00m, "USD", null);

        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.Equal("AUTH-1", order.AuthorizationId);
    }

    [Fact]
    public void CaptureThenPartialRefundLeavesRemainder()
    {
        var order = PaidOrder();
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", 20.00m, "USD", null);
        order.RecordCapture("CAP-1", "COMPLETED", 20.00m, 0.88m, 19.12m, "USD");

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(0.88m, order.PayPalFee);
        Assert.Equal(19.12m, order.NetAmount);

        var first = order.RecordRefund("RF-1", "key-1", 5.00m, "USD", "COMPLETED");
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(15.00m, order.RemainingRefundableAmount());

        var replay = order.RecordRefund("RF-1", "key-1", 5.00m, "USD", "COMPLETED");
        Assert.Same(first, replay);
        Assert.Equal(15.00m, order.RemainingRefundableAmount());

        order.RecordRefund("RF-2", "key-2", 15.00m, "USD", "COMPLETED");
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RemainingRefundableAmount());
    }

    [Fact]
    public void RejectsRefundBeyondCapturedAmount()
    {
        var order = PaidOrder();
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", 20.00m, "USD", null);
        order.RecordCapture("CAP-1", "COMPLETED", 20.00m, 0.88m, 19.12m, "USD");

        Assert.Throws<PaymentValidationException>(() =>
            order.RecordRefund("RF-1", "key-1", 20.01m, "USD", "COMPLETED"));
    }

    [Fact]
    public void CancelAfterFulfilmentIsRejected()
    {
        var order = PaidOrder();
        order.RecordAuthorization("PAYPAL-ORDER", "AUTH-1", "CREATED", 20.00m, "USD", null);
        order.RecordCapture("CAP-1", "COMPLETED", 20.00m, 0.88m, 19.12m, "USD");

        Assert.Throws<PaymentConflictException>(() => order.RecordCancellation());
    }
}
