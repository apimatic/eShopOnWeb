using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities;

public class OrderPaymentTests
{
    [Fact]
    public void NewOrderStartsAwaitingPaymentWithUniqueReference()
    {
        var first = NewOrder();
        var second = NewOrder();

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, first.PaymentStatus);
        Assert.Equal(OrderFulfillmentStatus.Pending, first.FulfillmentStatus);
        Assert.Equal("USD", first.Currency);
        Assert.StartsWith("ESHOP-", first.PaymentReference);
        Assert.NotEqual(first.PaymentReference, second.PaymentReference);
    }

    [Fact]
    public void CaptureAndPartialRefundRetainProcessorAmounts()
    {
        var order = NewOrder();
        var authorizedAt = DateTimeOffset.UtcNow;
        order.RecordPayPalOrder("PAYPAL-ORDER");
        order.RecordAuthorization("AUTH-1", "CREATED", 3.69m, authorizedAt, authorizedAt.AddDays(29));
        order.RecordCapture("CAPTURE-1", "COMPLETED", 3.69m, .41m, 3.28m, authorizedAt.AddMinutes(1));

        order.AddRefund("refund-one", "REFUND-1", 1m, "COMPLETED", authorizedAt.AddMinutes(2));
        order.AddRefund("refund-two", "REFUND-2", 1.50m, "COMPLETED", authorizedAt.AddMinutes(3));

        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(OrderFulfillmentStatus.Fulfilled, order.FulfillmentStatus);
        Assert.Equal(3.69m, order.CapturedAmount);
        Assert.Equal(.41m, order.PayPalFee);
        Assert.Equal(3.28m, order.NetAmount);
        Assert.Equal(2.50m, order.RefundedAmount);
        Assert.Equal(2, order.Refunds.Count);
    }

    [Fact]
    public void FullRefundAndCancellationHaveDistinctStates()
    {
        var captured = NewOrder();
        captured.RecordAuthorization("AUTH-1", "CREATED", 3.69m, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(29));
        captured.RecordCapture("CAPTURE-1", "COMPLETED", 3.69m, null, null, DateTimeOffset.UtcNow);
        captured.AddRefund("full", "REFUND-1", 3.69m, "COMPLETED", DateTimeOffset.UtcNow);

        var cancelled = NewOrder();
        cancelled.RecordAuthorization("AUTH-2", "CREATED", 3.69m, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(29));
        cancelled.Cancel("VOIDED");

        Assert.Equal(OrderPaymentStatus.Refunded, captured.PaymentStatus);
        Assert.Equal(OrderFulfillmentStatus.Fulfilled, captured.FulfillmentStatus);
        Assert.Equal(OrderPaymentStatus.Cancelled, cancelled.PaymentStatus);
        Assert.Equal(OrderFulfillmentStatus.Cancelled, cancelled.FulfillmentStatus);
        Assert.Equal("VOIDED", cancelled.AuthorizationStatus);
    }

    private static Order NewOrder()
    {
        var builder = new OrderBuilder();
        var item = new OrderItem(builder.TestCatalogItemOrdered, builder.TestUnitPrice, builder.TestUnits);
        return new Order(builder.TestBuyerId, new AddressBuilder().WithDefaultValues(), new() { item }, "USD");
    }
}
