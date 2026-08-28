using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void OrderStartsAwaitingPaymentAndUsesItsExistingTotal()
    {
        var order = CreateOrder();

        var payment = order.StartPayment("USD");

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(8.50m, payment.Amount);
        Assert.Equal("USD", payment.Currency);
        Assert.StartsWith("eshop-", payment.MerchantReference);
    }

    [Fact]
    public void CapturedPaymentCannotBeOverRefundedEvenWithPendingRefunds()
    {
        var order = CreateOrder();
        var payment = order.StartPayment("USD");
        payment.Authorize("AUTH-1", "CREATED", 8.50m, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(29), "COMPLETED");
        order.MarkAuthorized();
        payment.RecordCapture("CAPTURE-1", "COMPLETED", 8.50m, 0.71m, 7.79m, DateTimeOffset.UtcNow);
        order.MarkFulfilled();

        var first = payment.StartRefund("refund-one", 5m);

        Assert.Equal(3.50m, payment.RefundableAmount);
        Assert.Same(first, payment.FindRefund("refund-one"));
        Assert.Throws<InvalidOperationException>(() => payment.StartRefund("refund-two", 3.51m));
    }

    [Fact]
    public void PayPalAmountsMustMatchTheOrderExactly()
    {
        var payment = CreateOrder().StartPayment("USD");

        Assert.Throws<InvalidOperationException>(() => payment.Authorize("AUTH-1", "CREATED", 8.49m,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29), "COMPLETED"));
    }

    private static Order CreateOrder() => new("shopper@example.com",
        new Address("Street", "City", "State", "Country", "Zip"),
        new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "Mug", "mug.png"), 8.50m, 1)
        });
}
