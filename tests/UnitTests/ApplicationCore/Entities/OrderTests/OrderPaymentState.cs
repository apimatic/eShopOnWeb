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
    public void RecordsAuthorizationThenCapture()
    {
        var order = PaidOrder();
        order.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(3.69m, order.CapturedAmount);
        Assert.Equal(0.41m, order.PayPalFee);
        Assert.Equal(3.28m, order.NetAmount);
    }

    [Fact]
    public void PayIsIdempotentAfterAuthorization()
    {
        var order = PaidOrder();
        order.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", null, "USD");
        order.RecordAuthorization("PP-ORDER-2", "AUTH-2", "CREATED", null, "USD");

        Assert.Equal("AUTH-1", order.PayPalAuthorizationId);
    }

    [Fact]
    public void PartialRefundCannotExceedCapture()
    {
        var order = PaidOrder();
        order.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m);

        order.RecordRefund("R1", "COMPLETED", 1.00m, "key-1");
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(2.69m, order.RemainingRefundable());

        var replay = order.RecordRefund("R1-dup", "COMPLETED", 1.00m, "key-1");
        Assert.Equal("R1", replay.PayPalRefundId);

        var second = order.RecordRefund("R2", "COMPLETED", 2.69m, "key-2");
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RemainingRefundable());
        Assert.Equal("R2", second.PayPalRefundId);

        var ex = Assert.Throws<CheckoutException>(() => order.RecordRefund("R3", "COMPLETED", 0.01m, "key-3"));
        Assert.Equal("REFUND_EXCEEDS_CAPTURE", ex.Code);
    }

    [Fact]
    public void CancelAfterFulfilmentIsRejected()
    {
        var order = PaidOrder();
        order.RecordAuthorization("PP-ORDER", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m);

        Assert.Throws<CheckoutException>(() => order.Cancel());
    }

    private static Order PaidOrder()
    {
        var builder = new OrderBuilder();
        return builder.WithItems(new List<OrderItem>
        {
            new OrderItem(builder.TestCatalogItemOrdered, builder.TestUnitPrice, builder.TestUnits)
        });
    }
}
