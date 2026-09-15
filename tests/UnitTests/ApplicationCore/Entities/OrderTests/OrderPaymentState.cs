using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentState
{
    private static Order CreateOrder()
    {
        var item = new OrderItem(new CatalogItemOrdered(1, "Mug", "http://example.com/mug.png"), 8.50m, 2);
        return new Order("demouser@microsoft.com", new Address("1 Main", "San Jose", "CA", "US", "95131"), new() { item }, "USD");
    }

    [Fact]
    public void StartsAwaitingPayment()
    {
        var order = CreateOrder();
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal(17.00m, order.Total());
    }

    [Fact]
    public void RecordsAuthorizationThenCaptureAndPartialRefund()
    {
        var order = CreateOrder();
        order.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", DateTimeOffset.UtcNow.AddDays(3), "USD");
        Assert.Equal(OrderPaymentStatus.Authorized, order.PaymentStatus);

        order.RecordCapture("CAP-1", "COMPLETED", 17.00m, 0.66m, 16.34m);
        Assert.Equal(OrderPaymentStatus.Fulfilled, order.PaymentStatus);
        Assert.Equal(0.66m, order.PaypalFee);
        Assert.Equal(16.34m, order.NetAmount);

        order.AddRefund(new OrderRefund("REF-1", "key-1", "COMPLETED", 5.00m));
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(12.00m, order.RemainingRefundable());

        order.AddRefund(new OrderRefund("REF-2", "key-2", "COMPLETED", 12.00m));
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void RefundIdempotencyReturnsSameRefund()
    {
        var order = CreateOrder();
        order.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 17.00m, 0.66m, 16.34m);
        order.AddRefund(new OrderRefund("REF-1", "same-key", "COMPLETED", 5.00m));

        var found = order.FindRefundByIdempotencyKey("same-key");
        Assert.NotNull(found);
        Assert.Equal("REF-1", found!.PayPalRefundId);
    }

    [Fact]
    public void CannotRefundMoreThanCapturedViaRemaining()
    {
        var order = CreateOrder();
        order.RecordAuthorization("PAYPAL-ORDER", "COMPLETED", "AUTH-1", "CREATED", null, "USD");
        order.RecordCapture("CAP-1", "COMPLETED", 17.00m, 0.66m, 16.34m);
        order.AddRefund(new OrderRefund("REF-1", "key-1", "COMPLETED", 17.00m));
        Assert.Equal(0m, order.RemainingRefundable());
    }

    [Fact]
    public void CannotFulfilFromCancelled()
    {
        var order = CreateOrder();
        order.MarkCancelled();
        Assert.Throws<PaymentException>(() => order.RecordCapture("CAP-1", "COMPLETED", 17.00m, null, null));
    }
}
