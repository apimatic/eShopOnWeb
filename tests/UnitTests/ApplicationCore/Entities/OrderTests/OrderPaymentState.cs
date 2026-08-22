using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void StartsAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void RemainingRefundableAmountTracksPartialRefunds()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", System.DateTimeOffset.UtcNow, null, "USD");
        order.MarkFulfilled("CAPTURE-1", "COMPLETED", 3.69m, 0.41m, 3.28m);

        order.MarkRefunded("key-1", "REFUND-1", "COMPLETED", 1.00m);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(2.69m, order.Payment.RemainingRefundableAmount());

        var duplicate = order.MarkRefunded("key-1", "REFUND-1", "COMPLETED", 1.00m);
        Assert.Equal("REFUND-1", duplicate.PayPalRefundId);
        Assert.Equal(2.69m, order.Payment.RemainingRefundableAmount());

        order.MarkRefunded("key-2", "REFUND-2", "COMPLETED", 2.69m);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.Payment.RemainingRefundableAmount());
    }

    [Fact]
    public void CannotRefundMoreThanCaptured()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", System.DateTimeOffset.UtcNow, null, "USD");
        order.MarkFulfilled("CAPTURE-1", "COMPLETED", 3.69m, 0.41m, 3.28m);

        Assert.Throws<PaymentException>(() => order.MarkRefunded("key-1", "REFUND-1", "COMPLETED", 4.00m));
    }

    [Fact]
    public void CannotCancelAfterFulfilment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", System.DateTimeOffset.UtcNow, null, "USD");
        order.MarkFulfilled("CAPTURE-1", "COMPLETED", 3.69m, 0.41m, 3.28m);

        Assert.Throws<PaymentException>(() => order.MarkCancelled());
    }
}
