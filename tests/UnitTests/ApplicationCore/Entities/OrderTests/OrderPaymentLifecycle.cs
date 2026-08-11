using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentLifecycle
{
    private const string BuyerId = "buyer@example.com";
    private const string Currency = "USD";

    private static Order NewOrder()
    {
        var address = new Address("123 Main St", "Kent", "OH", "US", "44240");
        var items = new List<OrderItem>
        {
            new OrderItem(new CatalogItemOrdered(5, "Roslyn Red Sheet", "5.png"), 8.5m, 2), // 17.00
            new OrderItem(new CatalogItemOrdered(4, "Sweatshirt", "4.png"), 12m, 1),          // 12.00
        };
        return new Order(BuyerId, address, items); // total 29.00
    }

    [Fact]
    public void NewOrderAwaitsPaymentWithNoPayment()
    {
        var order = NewOrder();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Null(order.Payment);
        Assert.Equal(29.00m, order.Total());
    }

    [Fact]
    public void RecordAuthorizationHoldsTheOrderTotal()
    {
        var order = NewOrder();
        var expires = DateTimeOffset.UtcNow.AddDays(3);

        order.RecordAuthorization("PPORDER1", "AUTH1", "CREATED", Currency, expires);

        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.NotNull(order.Payment);
        Assert.Equal(29.00m, order.Payment!.Amount);
        Assert.Equal(Currency, order.Payment.Currency);
        Assert.Equal("AUTH1", order.Payment.AuthorizationId);
        Assert.True(order.Payment.IsAuthorized);
        Assert.False(order.Payment.IsCaptured);
    }

    [Fact]
    public void RecordFulfilmentCapturesWithFeeAndNet()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPORDER1", "AUTH1", "CREATED", Currency, null);

        order.RecordFulfilment("CAP1", "COMPLETED", 29.00m, 1.24m, 27.76m);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.True(order.Payment!.IsCaptured);
        Assert.Equal(29.00m, order.Payment.CapturedAmount);
        Assert.Equal(1.24m, order.Payment.PayPalFee);
        Assert.Equal(27.76m, order.Payment.NetAmount);
        Assert.Equal(29.00m, order.Payment.RefundableRemaining);
    }

    [Fact]
    public void CancelBeforeFulfilmentReleasesTheHold()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPORDER1", "AUTH1", "CREATED", Currency, null);

        order.RecordCancellation();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal("VOIDED", order.Payment!.AuthorizationStatus);
    }

    [Fact]
    public void CannotFulfilACancelledOrder()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPORDER1", "AUTH1", "CREATED", Currency, null);
        order.RecordCancellation();

        Assert.Throws<InvalidOperationException>(() =>
            order.RecordFulfilment("CAP1", "COMPLETED", 29.00m, 1.24m, 27.76m));
    }

    [Fact]
    public void CannotCancelAFulfilledOrder()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPORDER1", "AUTH1", "CREATED", Currency, null);
        order.RecordFulfilment("CAP1", "COMPLETED", 29.00m, 1.24m, 27.76m);

        Assert.Throws<InvalidOperationException>(() => order.RecordCancellation());
    }

    [Fact]
    public void PartialRefundsReduceRefundableRemaining()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPORDER1", "AUTH1", "CREATED", Currency, null);
        order.RecordFulfilment("CAP1", "COMPLETED", 29.00m, 1.24m, 27.76m);
        var payment = order.Payment!;

        payment.GuardRefundWithinCaptured(5m);
        payment.AddRefund("REF1", 5m, "COMPLETED", "key-1", DateTimeOffset.UtcNow);
        payment.GuardRefundWithinCaptured(3m);
        payment.AddRefund("REF2", 3m, "COMPLETED", "key-2", DateTimeOffset.UtcNow);

        Assert.Equal(8m, payment.TotalRefunded);
        Assert.Equal(21m, payment.RefundableRemaining);
    }

    [Fact]
    public void RefundBeyondCapturedIsRejected()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPORDER1", "AUTH1", "CREATED", Currency, null);
        order.RecordFulfilment("CAP1", "COMPLETED", 29.00m, 1.24m, 27.76m);
        var payment = order.Payment!;

        payment.AddRefund("REF1", 20m, "COMPLETED", "key-1", DateTimeOffset.UtcNow);

        // Remaining is 9.00; a 10.00 refund must be refused so the order never becomes refundable
        // beyond what was captured.
        Assert.Throws<InvalidOperationException>(() => payment.GuardRefundWithinCaptured(10m));
    }

    [Fact]
    public void RefundLookupByIdempotencyKeyFindsTheSameRefund()
    {
        var order = NewOrder();
        order.RecordAuthorization("PPORDER1", "AUTH1", "CREATED", Currency, null);
        order.RecordFulfilment("CAP1", "COMPLETED", 29.00m, 1.24m, 27.76m);
        var payment = order.Payment!;
        payment.AddRefund("REF1", 5m, "COMPLETED", "key-1", DateTimeOffset.UtcNow);

        var found = payment.FindRefundByIdempotencyKey("key-1");

        Assert.NotNull(found);
        Assert.Equal("REF1", found!.RefundId);
        Assert.Null(payment.FindRefundByIdempotencyKey("nope"));
    }

    [Fact]
    public void CannotRefundBeforeCapture()
    {
        var payment = new OrderPayment(29.00m, Currency);
        Assert.Throws<InvalidOperationException>(() => payment.GuardRefundWithinCaptured(1m));
    }
}
