using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void TracksAuthorizationFulfilmentAndRefundsOnExistingOrderModel()
    {
        var order = new Order("buyer@example.com", new Address("street", "city", "state", "US", "12345"),
            new List<OrderItem>
            {
                new(new CatalogItemOrdered(1, "item", "picture"), 12.50m, 2)
            });
        var payment = new OrderPayment(1, "buyer@example.com", order.Total(), "USD");

        order.MarkAuthorized();
        payment.RecordAuthorization("AUTH-1", "CREATED", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(29), "VISA", "1111", null, "COMPLETED");
        payment.RecordCapture("CAPTURE-1", "COMPLETED", 25m, 1m, 24m, DateTimeOffset.UtcNow);
        order.MarkFulfilled(DateTimeOffset.UtcNow);
        payment.AddRefund("refund-1", "REFUND-1", "COMPLETED", 10m, DateTimeOffset.UtcNow);
        order.MarkRefunded(false);
        payment.AddRefund("refund-2", "REFUND-2", "COMPLETED", 15m, DateTimeOffset.UtcNow);
        order.MarkRefunded(true);

        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(25m, payment.RefundedAmount);
        Assert.Equal(2, payment.Refunds.Count);
    }

    [Fact]
    public void RepeatingARefundKeyDoesNotCreateAnotherRefund()
    {
        var payment = new OrderPayment(1, "buyer@example.com", 25m, "USD");

        var first = payment.AddRefund("same-key", "REFUND-1", "COMPLETED", 5m, DateTimeOffset.UtcNow);
        var repeated = payment.AddRefund("same-key", "REFUND-2", "COMPLETED", 5m, DateTimeOffset.UtcNow);

        Assert.Same(first, repeated);
        Assert.Single(payment.Refunds);
        Assert.Equal("REFUND-1", repeated.PayPalRefundId);
    }
}
