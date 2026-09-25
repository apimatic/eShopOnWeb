using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities;

public class OrderPaymentTests
{
    private static Order NewOrder(decimal unitPrice = 10m, int units = 2)
    {
        var address = new Address("street", "city", "state", "country", "12345");
        var item = new OrderItem(new CatalogItemOrdered(1, "Widget", "pic.png"), unitPrice, units);
        return new Order("buyer@example.com", address, new List<OrderItem> { item });
    }

    [Fact]
    public void NewOrder_IsAwaitingPayment_WithNoPayment()
    {
        var order = NewOrder();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void Authorize_FromAwaitingPayment_SetsAuthorizedAndPayment()
    {
        var order = NewOrder();
        var expiry = DateTimeOffset.UtcNow.AddDays(3);

        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", expiry, "USD", savedPaymentMethodId: null);

        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.NotNull(order.Payment);
        Assert.Equal("AUTH-1", order.Payment!.AuthorizationId);
        Assert.Equal(20m, order.Payment.Amount);
        Assert.Equal("USD", order.Payment.Currency);
    }

    [Fact]
    public void Authorize_Twice_Throws()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, "USD", null);
        Assert.Throws<InvalidOrderPaymentStateException>(() =>
            order.RecordAuthorization("PPO-2", "AUTH-2", "CREATED", null, "USD", null));
    }

    [Fact]
    public void Fulfil_BeforeAuthorize_Throws()
    {
        var order = NewOrder();
        Assert.Throws<InvalidOrderPaymentStateException>(() =>
            order.RecordCapture("CAP-1", "COMPLETED", 20m, 1m, 19m));
    }

    [Fact]
    public void Capture_FromAuthorized_SetsFulfilledWithFigures()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, "USD", null);

        order.RecordCapture("CAP-1", "COMPLETED", 20m, 0.88m, 19.12m);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal("CAP-1", order.Payment!.CaptureId);
        Assert.Equal(20m, order.Payment.CapturedGross);
        Assert.Equal(0.88m, order.Payment.PaypalFee);
        Assert.Equal(19.12m, order.Payment.NetAmount);
    }

    [Fact]
    public void Cancel_FromAuthorized_ReleasesFunds()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, "USD", null);

        order.RecordCancellation();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("VOIDED", order.Payment!.AuthorizationStatus);
    }

    [Fact]
    public void Cancel_AfterFulfil_Throws()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, "USD", null);
        order.RecordCapture("CAP-1", "COMPLETED", 20m, 1m, 19m);
        Assert.Throws<InvalidOrderPaymentStateException>(() => order.RecordCancellation());
    }

    [Fact]
    public void Refund_Full_MarksRefunded()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, "USD", null);
        order.RecordCapture("CAP-1", "COMPLETED", 20m, 1m, 19m);

        order.RecordRefund("key-1", "REF-1", 20m, "COMPLETED");

        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RefundableRemaining());
    }

    [Fact]
    public void Refund_Partial_MarksPartiallyRefunded_ThenCaps()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, "USD", null);
        order.RecordCapture("CAP-1", "COMPLETED", 20m, 1m, 19m);

        order.RecordRefund("key-1", "REF-1", 8m, "COMPLETED");
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(12m, order.RefundableRemaining());

        // A second partial refund is legitimate...
        order.RecordRefund("key-2", "REF-2", 12m, "COMPLETED");
        Assert.Equal(OrderStatus.Refunded, order.Status);

        // ...but refunding beyond the captured amount is rejected.
        Assert.Throws<InvalidOrderPaymentStateException>(() =>
            order.RecordRefund("key-3", "REF-3", 0.01m, "COMPLETED"));
    }

    [Fact]
    public void Refund_OverCapturedAmount_Throws()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, "USD", null);
        order.RecordCapture("CAP-1", "COMPLETED", 20m, 1m, 19m);

        Assert.Throws<InvalidOrderPaymentStateException>(() =>
            order.RecordRefund("key-1", "REF-1", 20.01m, "COMPLETED"));
    }

    [Fact]
    public void FindRefundByKey_ReturnsRecordedRefund()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPO-1", "AUTH-1", "CREATED", null, "USD", null);
        order.RecordCapture("CAP-1", "COMPLETED", 20m, 1m, 19m);
        order.RecordRefund("key-1", "REF-1", 5m, "COMPLETED");

        Assert.NotNull(order.FindRefundByKey("key-1"));
        Assert.Null(order.FindRefundByKey("nope"));
    }
}
