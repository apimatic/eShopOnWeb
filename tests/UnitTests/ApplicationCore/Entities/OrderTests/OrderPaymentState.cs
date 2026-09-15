using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void NewOrderStartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void RecordAuthorizationIsIdempotent()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var payment = CreatePayment();
        order.RecordAuthorization(payment);
        order.RecordAuthorization(CreatePayment("other"));

        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.Equal("AUTH-1", order.Payment!.AuthorizationId);
    }

    [Fact]
    public void RefundCannotExceedCapturedAmount()
    {
        var order = FulfilledOrder();
        order.RecordRefund("R1", "COMPLETED", 2.00m, "USD", "key-1");

        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(1.69m, order.RemainingRefundableAmount());

        Assert.Throws<PaymentException>(() => order.RecordRefund("R2", "COMPLETED", 2.00m, "USD", "key-2"));
    }

    [Fact]
    public void SameIdempotencyKeyDoesNotRefundTwice()
    {
        var order = FulfilledOrder();
        var first = order.RecordRefund("R1", "COMPLETED", 1.00m, "USD", "same-key");
        var second = order.RecordRefund("R2", "COMPLETED", 1.00m, "USD", "same-key");

        Assert.Same(first, second);
        Assert.Equal(1.00m, order.RefundedAmount());
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
    }

    [Fact]
    public void DistinctPartialRefundsAreAllowedUntilCapturedTotal()
    {
        var order = FulfilledOrder();
        order.RecordRefund("R1", "COMPLETED", 1.50m, "USD", "key-a");
        order.RecordRefund("R2", "COMPLETED", 2.19m, "USD", "key-b");

        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RemainingRefundableAmount());
    }

    [Fact]
    public void FulfilledOrderCannotBeCancelled()
    {
        var order = FulfilledOrder();
        Assert.Throws<PaymentException>(() => order.RecordCancellation());
    }

    private static Order FulfilledOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization(CreatePayment());
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m);
        return order;
    }

    private static OrderPayment CreatePayment(string authorizationId = "AUTH-1")
    {
        return new OrderPayment("ORDER-1", "COMPLETED", authorizationId, "CREATED", null, null, "USD", 3.69m);
    }
}
