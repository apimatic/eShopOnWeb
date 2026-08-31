using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void PaidOrderStartsAwaitingPaymentWithUniqueReference()
    {
        var order = PaidOrder();

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
        Assert.Equal("USD", order.Currency);
        Assert.NotEmpty(order.PaymentReference!);
    }

    [Fact]
    public void CaptureStoresPayPalEconomics()
    {
        var order = AuthorizedOrder();
        var capturedAt = DateTimeOffset.UtcNow;

        order.RecordCapture("CAPTURE-1", "COMPLETED", order.Total(), 0.44m,
            order.Total() - 0.44m, capturedAt);

        Assert.Equal(OrderPaymentStatus.Fulfilled, order.PaymentStatus);
        Assert.Equal("CAPTURE-1", order.CaptureId);
        Assert.Equal(0.44m, order.PayPalFee);
        Assert.Equal(order.Total() - 0.44m, order.NetProceeds);
    }

    [Fact]
    public void DistinctPartialRefundsCannotExceedCapture()
    {
        var order = AuthorizedOrder();
        order.RecordCapture("CAPTURE-1", "COMPLETED", order.Total(), 0.44m,
            order.Total() - 0.44m, DateTimeOffset.UtcNow);

        order.AddRefund("REFUND-1", "key-1", "COMPLETED", 1m,
            DateTimeOffset.UtcNow);
        order.AddRefund("REFUND-2", "key-2", "COMPLETED", order.Total() - 1m,
            DateTimeOffset.UtcNow);

        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(order.Total(), order.RefundedAmount);
        Assert.Throws<InvalidOperationException>(() => order.AddRefund(
            "REFUND-3", "key-3", "COMPLETED", 0.01m, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ReauthorizationCannotExtendOriginalTwentyNineDayWindow()
    {
        var order = AuthorizedOrder();
        var originalDeadline = order.AuthorizationExpiresAt;

        order.RecordReauthorization("AUTH-2", "CREATED", DateTimeOffset.UtcNow.AddDays(4),
            DateTimeOffset.UtcNow.AddDays(33), order.Total());

        Assert.Equal(originalDeadline, order.AuthorizationExpiresAt);
    }

    private static Order PaidOrder()
    {
        var builder = new OrderBuilder();
        var item = new OrderItem(builder.TestCatalogItemOrdered, builder.TestUnitPrice,
            builder.TestUnits);
        return new Order(builder.TestBuyerId, new AddressBuilder().WithDefaultValues(),
            new List<OrderItem> { item }, "USD");
    }

    private static Order AuthorizedOrder()
    {
        var order = PaidOrder();
        var now = DateTimeOffset.UtcNow;
        order.StartPayment("ORDER-1", "CREATED", null);
        order.RecordAuthorization("AUTH-1", "CREATED", now, now.AddDays(29), order.Total());
        return order;
    }
}
