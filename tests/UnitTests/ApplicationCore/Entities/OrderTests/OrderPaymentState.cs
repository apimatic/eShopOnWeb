using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    [Fact]
    public void NewOrderAwaitsPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void MarkAuthorizedSetsHold()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", "2030-01-01T00:00:00Z", "2026-01-01T00:00:00Z", "USD");

        Assert.Equal(OrderPaymentStatus.Authorized, order.Status);
        Assert.Equal("AUTH-1", order.AuthorizationId);
        Assert.Equal("PAYPAL-ORDER", order.PayPalOrderId);
    }

    [Fact]
    public void FulfilThenPartialRefundLeavesRemainder()
    {
        var order = AuthorizedThenFulfilled();
        order.RecordRefund("REF-1", "key-1", 1.00m, "USD", "COMPLETED");

        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.Status);
        Assert.Equal(2.69m, order.RefundableRemaining());
    }

    [Fact]
    public void PartialRefundCannotExceedCaptured()
    {
        var order = AuthorizedThenFulfilled();
        order.RecordRefund("REF-1", "key-1", 1.00m, "USD", "COMPLETED");

        Assert.Throws<PaymentException>(() =>
            order.RecordRefund("REF-2", "key-2", 3.00m, "USD", "COMPLETED"));
    }

    [Fact]
    public void FullRefundClosesTheOrder()
    {
        var order = AuthorizedThenFulfilled();
        order.RecordRefund("REF-1", "key-1", 3.69m, "USD", "COMPLETED");

        Assert.Equal(OrderPaymentStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RefundableRemaining());
    }

    [Fact]
    public void SameIdempotencyKeyIsFindable()
    {
        var order = AuthorizedThenFulfilled();
        order.RecordRefund("REF-1", "same-key", 1.00m, "USD", "COMPLETED");

        var found = order.FindRefundByIdempotencyKey("same-key");
        Assert.NotNull(found);
        Assert.Equal("REF-1", found!.PayPalRefundId);
    }

    [Fact]
    public void CancelAfterFulfilIsRejected()
    {
        var order = AuthorizedThenFulfilled();
        Assert.Throws<PaymentException>(() => order.MarkCancelled());
    }

    private static Order AuthorizedThenFulfilled()
    {
        var builder = new OrderBuilder();
        var items = new List<OrderItem>
        {
            new OrderItem(builder.TestCatalogItemOrdered, 1.23m, 3)
        };
        var order = builder.WithItems(items);
        order.MarkAuthorized("PAYPAL-ORDER", "AUTH-1", "CREATED", null, null, "USD");
        order.MarkFulfilled("CAP-1", "COMPLETED", 3.69m, 0.11m, 3.58m);
        return order;
    }
}
