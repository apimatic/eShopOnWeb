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
    public void RecordsAuthorizationAndIsIdempotentForSameHold()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("ORDER1", "AUTH1", "CREATED", null, null);
        order.RecordAuthorization("ORDER1", "AUTH1", "CREATED", null, null);

        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.Equal("AUTH1", order.PayPalAuthorizationId);
    }

    [Fact]
    public void CaptureThenPartialRefundLeavesRemainder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("ORDER1", "AUTH1", "CREATED", null, null);
        order.RecordCapture("CAP1", "COMPLETED", 3.69m, 0.20m, 3.49m);

        var first = order.RecordRefund("REF1", 1.00m, "COMPLETED", "key-1");
        var replay = order.RecordRefund("REF1-ignored", 1.00m, "COMPLETED", "key-1");

        Assert.Same(first, replay);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(2.69m, order.RefundableAmount());
    }

    [Fact]
    public void CannotRefundMoreThanCaptured()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("ORDER1", "AUTH1", "CREATED", null, null);
        order.RecordCapture("CAP1", "COMPLETED", 3.69m, 0.20m, 3.49m);

        Assert.Throws<CheckoutException>(() => order.RecordRefund("REF1", 4.00m, "COMPLETED", "key-1"));
    }

    [Fact]
    public void DistinctRefundKeysAreBothAcceptedUntilCapturedAmountIsExhausted()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.RecordAuthorization("ORDER1", "AUTH1", "CREATED", null, null);
        order.RecordCapture("CAP1", "COMPLETED", 3.69m, 0.20m, 3.49m);
        order.RecordRefund("REF1", 1.00m, "COMPLETED", "key-1");
        order.RecordRefund("REF2", 2.69m, "COMPLETED", "key-2");

        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RefundableAmount());
        Assert.Equal(2, order.Refunds.Count);
    }
}
