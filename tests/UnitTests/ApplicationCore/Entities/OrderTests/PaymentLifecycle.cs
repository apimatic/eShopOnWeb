using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void PayableOrderStartsAwaitingPaymentWithoutChangingLegacyOrders()
    {
        var builder = new OrderBuilder();
        var legacy = builder.WithDefaultValues();
        var payable = PayableOrder(builder);

        Assert.Equal(PaymentStatus.NotRequired, legacy.PaymentStatus);
        Assert.Equal(PaymentStatus.AwaitingPayment, payable.PaymentStatus);
        Assert.Equal(FulfillmentStatus.AwaitingFulfillment, payable.FulfillmentStatus);
        Assert.Equal("USD", payable.Currency);
        Assert.StartsWith("eshop-", payable.PaymentReference);
    }

    [Fact]
    public void AuthorizationMustExactlyMatchOrderTotal()
    {
        var order = PayableOrder(new OrderBuilder());

        Assert.Throws<InvalidOperationException>(() =>
            order.RecordAuthorization("authorization", "CREATED", order.Total() + .01m,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));
    }

    [Fact]
    public void CaptureStoresProcessorBreakdownAndFulfilsOrder()
    {
        var order = AuthorizedOrder();

        order.RecordCapture("capture", "COMPLETED", order.Total(), .50m, order.Total() - .50m,
            DateTimeOffset.UtcNow);

        Assert.Equal(PaymentStatus.Captured, order.PaymentStatus);
        Assert.Equal(FulfillmentStatus.Fulfilled, order.FulfillmentStatus);
        Assert.Equal(.50m, order.PayPalFee);
        Assert.Equal(order.Total() - .50m, order.NetProceeds);
    }

    [Fact]
    public void DistinctPartialRefundsCannotExceedCapture()
    {
        var order = AuthorizedOrder();
        order.RecordCapture("capture", "COMPLETED", order.Total(), .50m, order.Total() - .50m,
            DateTimeOffset.UtcNow);
        var first = order.StartRefund(1m, "first", "request-1");
        order.CompleteRefund(first, "refund-1", "COMPLETED", 1m, DateTimeOffset.UtcNow);

        Assert.Equal(PaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Throws<InvalidOperationException>(() =>
            order.StartRefund(order.Total(), "second", "request-2"));

        var remainder = order.StartRefund(order.Total() - 1m, "second", "request-2");
        order.CompleteRefund(remainder, "refund-2", "COMPLETED", order.Total() - 1m, DateTimeOffset.UtcNow);
        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
    }

    private static Order AuthorizedOrder()
    {
        var order = PayableOrder(new OrderBuilder());
        order.RecordPayPalOrder("paypal-order", "CREATED");
        order.RecordAuthorization("authorization", "CREATED", order.Total(), DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(29));
        return order;
    }

    private static Order PayableOrder(OrderBuilder builder)
    {
        var item = new OrderItem(builder.TestCatalogItemOrdered, builder.TestUnitPrice, builder.TestUnits);
        return new Order(builder.TestBuyerId, new AddressBuilder().WithDefaultValues(), new List<OrderItem> { item },
            true, "USD");
    }
}
