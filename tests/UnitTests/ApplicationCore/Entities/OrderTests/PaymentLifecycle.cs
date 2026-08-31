using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void SupportsCaptureAndDistinctIdempotentPartialRefunds()
    {
        var order = NewOrder();
        var payment = order.StartPayment("USD", "create-request");
        var now = DateTimeOffset.UtcNow;
        payment.RecordPayPalOrder("paypal-order", "CREATED", now);
        payment.RecordAuthorization("COMPLETED", "authorization", "CREATED", 10m, now,
            now.AddDays(29), "VISA", "1111", now);
        order.MarkAuthorized();
        payment.StartCapture(now);
        payment.RecordCapture("capture", "COMPLETED", 10m, .59m, 9.41m, now, now);
        order.MarkFulfilled();

        var first = payment.StartRefund("first", "request-first", 3m, now);
        first.RecordResult("refund-one", "COMPLETED", 3m, now);
        var repeated = payment.StartRefund("first", "request-first", 3m, now);
        var second = payment.StartRefund("second", "request-second", 2m, now);
        second.RecordResult("refund-two", "COMPLETED", 2m, now);
        payment.UpdateRefundTotals(now);
        order.UpdateRefundState();

        Assert.Same(first, repeated);
        Assert.Equal(5m, payment.RefundedAmount);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Throws<InvalidOperationException>(() =>
            payment.StartRefund("too-much", "request-too-much", 6m, now));
    }

    [Fact]
    public void FullRefundTransitionsOrderAndPaymentToRefunded()
    {
        var order = NewOrder();
        var payment = order.StartPayment("USD", "create-request");
        var now = DateTimeOffset.UtcNow;
        payment.RecordPayPalOrder("paypal-order", "CREATED", now);
        payment.RecordAuthorization("COMPLETED", "authorization", "CREATED", 10m, now,
            now.AddDays(29), "VISA", "1111", now);
        order.MarkAuthorized();
        payment.StartCapture(now);
        payment.RecordCapture("capture", "COMPLETED", 10m, .59m, 9.41m, now, now);
        order.MarkFulfilled();
        var refund = payment.StartRefund("full", "request-full", 10m, now);
        refund.RecordResult("refund", "COMPLETED", 10m, now);
        payment.UpdateRefundTotals(now);
        order.UpdateRefundState();

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }

    private static Order NewOrder() => new(
        "shopper@example.com",
        new Address("street", "city", "state", "US", "12345"),
        new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "item", "picture"), 10m, 1)
        });
}
