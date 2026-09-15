using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void AuthorizeRejectsAmountDifferentFromCatalogTotal()
    {
        var order = new OrderBuilder().WithDefaultValues();

        Assert.Throws<InvalidOperationException>(() => order.Authorize("paypal-order", "COMPLETED", "auth",
            "CREATED", order.Total() + .01m, "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29)));
    }

    [Fact]
    public void CapturePersistsReportedFeeAndNet()
    {
        var order = AuthorizedOrder();

        order.RecordCapture("capture", "COMPLETED", order.Total(), .40m, order.Total() - .40m, DateTimeOffset.UtcNow);

        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal(PaymentStatus.Captured, order.PaymentStatus);
        Assert.Equal(.40m, order.PayPalFee);
        Assert.Equal(order.Total() - .40m, order.NetAmount);
    }

    [Fact]
    public void PartialRefundsCannotExceedCapture()
    {
        var order = AuthorizedOrder();
        order.RecordCapture("capture", "COMPLETED", order.Total(), .40m, order.Total() - .40m, DateTimeOffset.UtcNow);
        order.AddRefund("refund-one", "paypal-refund-one", 1m, "USD", "COMPLETED", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => order.AddRefund("refund-two", "paypal-refund-two",
            order.Total(), "USD", "COMPLETED", DateTimeOffset.UtcNow));
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
    }

    [Fact]
    public void CancellingAuthorizationRecordsVoidedState()
    {
        var order = AuthorizedOrder();

        order.Cancel("VOIDED");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Voided, order.PaymentStatus);
    }

    private static Order AuthorizedOrder()
    {
        var order = new OrderBuilder().WithDefaultValues();
        order.Authorize("paypal-order", "COMPLETED", "auth", "CREATED", order.Total(), "USD",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29));
        return order;
    }
}
