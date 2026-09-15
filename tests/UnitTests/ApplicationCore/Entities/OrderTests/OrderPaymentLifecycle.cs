using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentLifecycle
{
    private const string Currency = "USD";
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    private static Order NewOrder(decimal unitPrice = 25.50m, int qty = 2)
    {
        var address = new Address("1 Main", "Seattle", "WA", "USA", "98101");
        var items = new List<OrderItem>
        {
            new OrderItem(new CatalogItemOrdered(1, "Widget", "pic.png"), unitPrice, qty)
        };
        return new Order("buyer@example.com", address, items);
    }

    private static Order Authorized()
    {
        var order = NewOrder();
        order.RecordAuthorization(Currency, "PPO-1", "AUTH-1", "CREATED", Now, Now.AddDays(29), "REF-1");
        return order;
    }

    private static Order Captured(decimal captured = 51.00m)
    {
        var order = Authorized();
        order.RecordCapture("CAP-1", "COMPLETED", captured, 1.81m, 49.19m, Now);
        return order;
    }

    [Fact]
    public void NewOrder_StartsPendingPayment_WithNoPayment()
    {
        var order = NewOrder();
        Assert.Equal(OrderPaymentStatus.PendingPayment, order.PaymentStatus);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void RecordAuthorization_MovesToAuthorized_AndStoresHoldState()
    {
        var order = Authorized();

        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
        Assert.NotNull(order.Payment);
        Assert.Equal("PPO-1", order.Payment!.PayPalOrderId);
        Assert.Equal("AUTH-1", order.Payment.AuthorizationId);
        Assert.Equal("REF-1", order.Payment.CustomReference);
        Assert.Equal(Now.AddDays(29), order.Payment.AuthorizationExpiresAt);
    }

    [Fact]
    public void RecordReauthorization_ReplacesAuthorizationId_AndExpiry()
    {
        var order = Authorized();
        order.RecordReauthorization("AUTH-2", "CREATED", Now.AddDays(3));

        Assert.Equal("AUTH-2", order.Payment!.AuthorizationId);
        Assert.Equal(Now.AddDays(3), order.Payment.AuthorizationExpiresAt);
        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);
    }

    [Fact]
    public void RecordCapture_MovesToPaid_AndStoresFeeAndNet()
    {
        var order = Captured();

        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal("CAP-1", order.Payment!.CaptureId);
        Assert.Equal(51.00m, order.Payment.CapturedAmount);
        Assert.Equal(1.81m, order.Payment.PayPalFee);
        Assert.Equal(49.19m, order.Payment.NetAmount);
        Assert.Equal(51.00m, order.Payment.RemainingRefundable());
    }

    [Fact]
    public void RecordVoid_MovesToCancelled_AndMarksAuthorizationVoided()
    {
        var order = Authorized();
        order.RecordVoid();

        Assert.Equal(OrderPaymentStatus.Cancelled, order.PaymentStatus);
        Assert.Equal("VOIDED", order.Payment!.AuthorizationStatus);
    }

    [Fact]
    public void CancelBeforeAuthorization_MovesToCancelled_WithoutPayment()
    {
        var order = NewOrder();
        order.CancelBeforeAuthorization();

        Assert.Equal(OrderPaymentStatus.Cancelled, order.PaymentStatus);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void PartialRefund_MovesToPartiallyRefunded_AndReducesRemaining()
    {
        var order = Captured(51.00m);
        order.RecordRefund("RF-1", 10.00m, "COMPLETED", "keyA", Now);

        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(10.00m, order.Payment!.TotalRefunded());
        Assert.Equal(41.00m, order.Payment.RemainingRefundable());
    }

    [Fact]
    public void TwoDistinctPartialRefunds_Accumulate()
    {
        var order = Captured(51.00m);
        order.RecordRefund("RF-1", 10.00m, "COMPLETED", "keyA", Now);
        order.RecordRefund("RF-2", 5.00m, "COMPLETED", "keyB", Now);

        Assert.Equal(15.00m, order.Payment!.TotalRefunded());
        Assert.Equal(36.00m, order.Payment.RemainingRefundable());
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
    }

    [Fact]
    public void RefundingRemainder_MovesToFullyRefunded()
    {
        var order = Captured(51.00m);
        order.RecordRefund("RF-1", 51.00m, "COMPLETED", "keyA", Now);

        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.Payment!.RemainingRefundable());
    }

    [Fact]
    public void RemainingRefundable_NeverNegative()
    {
        var order = Captured(20.00m);
        order.RecordRefund("RF-1", 20.00m, "COMPLETED", "keyA", Now);

        Assert.Equal(0m, order.Payment!.RemainingRefundable());
    }

    [Fact]
    public void FindRefundByKey_ReturnsRecordedRefund_ForIdempotency()
    {
        var order = Captured();
        order.RecordRefund("RF-1", 10.00m, "COMPLETED", "keyA", Now);

        Assert.True(order.Payment!.HasRefundWithKey("keyA"));
        Assert.False(order.Payment.HasRefundWithKey("keyB"));
        Assert.Equal("RF-1", order.Payment.FindRefundByKey("keyA")!.RefundId);
    }
}
