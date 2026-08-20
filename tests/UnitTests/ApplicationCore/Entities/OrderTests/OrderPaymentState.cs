using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
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
    public void CancelFromAwaitingPaymentDoesNotRequireGatewayHold()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkCancelled();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.True(order.IsAlreadyCancelled());
    }

    [Fact]
    public void CannotFulfilUntilAuthorized()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Throws<System.InvalidOperationException>(() => order.MarkFulfilled());
    }

    [Fact]
    public void RefundsCannotExceedCapturedAmount()
    {
        var order = new OrderBuilder().WithDefaultValues();
        typeof(Order).GetProperty("Id")!.SetValue(order, 1);
        var payment = order.EnsurePayment("USD");
        payment.RecordAuthorization("AUTH-1", "CREATED", 42m, null, System.DateTimeOffset.UtcNow);
        order.MarkAuthorized();
        payment.RecordCapture("CAP-1", "COMPLETED", 42m, 1.20m, 40.80m, System.DateTimeOffset.UtcNow);
        order.MarkFulfilled();

        payment.AddRefund("R-1", 20m, "COMPLETED", "key-1");
        order.MarkRefunded();
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(22m, payment.RefundableRemaining);

        Assert.Throws<System.InvalidOperationException>(() => payment.AddRefund("R-2", 23m, "COMPLETED", "key-2"));
    }
}
