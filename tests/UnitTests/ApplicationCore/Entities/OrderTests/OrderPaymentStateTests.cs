using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentStateTests
{
    private static Order CreateOrder() => new("shopper@example.com",
        new Address("street", "city", "state", "US", "12345"),
        new List<OrderItem>
        {
            new(new CatalogItemOrdered(1, "item", "picture"), 10.25m, 2)
        });

    [Fact]
    public void StartsAwaitingPaymentWithCatalogDerivedTotal()
    {
        var order = CreateOrder();

        Assert.Equal(OrderPaymentState.AwaitingPayment, order.PaymentState);
        Assert.Equal(20.50m, order.Total());
    }

    [Fact]
    public void PaymentReferenceRequiresAPersistedOrderIdentity()
    {
        var order = CreateOrder();

        Assert.Throws<System.InvalidOperationException>(() => order.EnsurePaymentReference());
    }

    [Fact]
    public void CaptureAndDistinctPartialRefundsTrackRemainingMoney()
    {
        var order = CreateOrder();
        order.RecordAuthorization("authorization", "CREATED", null, null);
        order.RecordCapture("capture", "COMPLETED", 20.50m, 1.25m, 19.25m);

        var first = order.AddRefund("first-key", "first-provider-key", 5m);
        first.RecordProviderResult("refund-1", "COMPLETED", 5m);
        order.ApplyCompletedRefund(5m);
        var second = order.AddRefund("second-key", "second-provider-key", 15.50m);
        second.RecordProviderResult("refund-2", "COMPLETED", 15.50m);
        order.ApplyCompletedRefund(15.50m);

        Assert.Equal(OrderPaymentState.Refunded, order.PaymentState);
        Assert.Equal(20.50m, order.RefundedAmount);
        Assert.Equal(2, order.Refunds.Count);
        Assert.Equal(1.25m, order.PayPalFee);
        Assert.Equal(19.25m, order.NetProceeds);
    }
}
