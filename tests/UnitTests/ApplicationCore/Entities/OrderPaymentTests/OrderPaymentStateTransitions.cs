using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderPaymentTests;

public class OrderPaymentStateTransitions
{
    private static Order NewOrder(string currency = "USD")
    {
        var items = new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Widget", "pic.png"), 10m, 2) // total 20.00
        };
        return new Order("buyer-1", new Address("1 St", "City", "ST", "US", "00000"), items, currency);
    }

    [Fact]
    public void NewOrderIsAwaitingPaymentWithConfiguredCurrency()
    {
        var order = NewOrder("EUR");
        Assert.Equal(PaymentStatus.AwaitingPayment, order.Payment.Status);
        Assert.Equal("EUR", order.Payment.Currency);
        Assert.Equal(20m, order.Total());
    }

    [Fact]
    public void RecordAuthorizationMovesToAuthorized()
    {
        var order = NewOrder();
        order.Payment.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, "VISA ****1111");

        Assert.Equal(PaymentStatus.Authorized, order.Payment.Status);
        Assert.Equal("PPO-1", order.Payment.PayPalOrderId);
        Assert.Equal("AUTH-1", order.Payment.AuthorizationId);
        Assert.Equal("VISA ****1111", order.Payment.InstrumentDescription);
    }

    [Fact]
    public void RecordCaptureStoresMoneyBreakdown()
    {
        var order = NewOrder();
        order.Payment.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, null);
        order.Payment.RecordCapture("CAP-1", "COMPLETED", 20m, 1.02m, 18.98m);

        Assert.Equal(PaymentStatus.Captured, order.Payment.Status);
        Assert.Equal(20m, order.Payment.CapturedAmount);
        Assert.Equal(1.02m, order.Payment.PayPalFee);
        Assert.Equal(18.98m, order.Payment.NetAmount);
        Assert.Equal(20m, order.Payment.RefundableAmount);
    }

    [Fact]
    public void PartialRefundThenFullRefundTransitionsStatuses()
    {
        var order = NewOrder();
        order.Payment.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, null);
        order.Payment.RecordCapture("CAP-1", "COMPLETED", 20m, 1m, 19m);

        order.Payment.RecordRefund(new PaymentRefund("REF-1", 5m, "COMPLETED", "k1"));
        Assert.Equal(PaymentStatus.PartiallyRefunded, order.Payment.Status);
        Assert.Equal(5m, order.Payment.RefundedAmount);
        Assert.Equal(15m, order.Payment.RefundableAmount);

        order.Payment.RecordRefund(new PaymentRefund("REF-2", 15m, "COMPLETED", "k2"));
        Assert.Equal(PaymentStatus.Refunded, order.Payment.Status);
        Assert.Equal(20m, order.Payment.RefundedAmount);
        Assert.Equal(0m, order.Payment.RefundableAmount);
    }

    [Fact]
    public void FindRefundByIdempotencyKeyReturnsExisting()
    {
        var order = NewOrder();
        order.Payment.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, null);
        order.Payment.RecordCapture("CAP-1", "COMPLETED", 20m, 1m, 19m);
        order.Payment.RecordRefund(new PaymentRefund("REF-1", 5m, "COMPLETED", "the-key"));

        Assert.NotNull(order.Payment.FindRefundByIdempotencyKey("the-key"));
        Assert.Null(order.Payment.FindRefundByIdempotencyKey("other-key"));
    }

    [Fact]
    public void CancellationMovesToCancelledAndVoidsAuthorization()
    {
        var order = NewOrder();
        order.Payment.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, null);
        order.Payment.RecordCancellation();

        Assert.Equal(PaymentStatus.Cancelled, order.Payment.Status);
        Assert.Equal("VOIDED", order.Payment.AuthorizationStatus);
    }
}
