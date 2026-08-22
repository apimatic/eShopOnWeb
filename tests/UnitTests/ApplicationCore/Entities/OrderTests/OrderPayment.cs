using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayment
{
    private readonly OrderBuilder _builder = new();

    [Fact]
    public void StartsAwaitingPayment()
    {
        var order = _builder.WithDefaultValues();
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void RecordAuthorizationIsIdempotentForSameAuth()
    {
        var order = _builder.WithDefaultValues();
        order.RecordAuthorization("O-1", "COMPLETED", "A-1", "CREATED", null, null, "USD");
        order.RecordAuthorization("O-1", "COMPLETED", "A-1", "CREATED", null, null, "USD");
        Assert.Equal(OrderStatus.Authorized, order.Status);
        Assert.Equal("A-1", order.PayPalAuthorizationId);
    }

    [Fact]
    public void CancelReleasesAuthorizedOrder()
    {
        var order = _builder.WithDefaultValues();
        order.RecordAuthorization("O-1", "COMPLETED", "A-1", "CREATED", null, null, "USD");
        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void CannotCancelAfterCapture()
    {
        var order = _builder.WithDefaultValues();
        order.RecordAuthorization("O-1", "COMPLETED", "A-1", "CREATED", null, null, "USD");
        order.RecordCapture("C-1", "COMPLETED", 3.69m, 0.20m, 3.49m, "USD");
        Assert.Throws<PaymentException>(() => order.Cancel());
    }

    [Fact]
    public void PartialRefundDoesNotExceedCapturedAmount()
    {
        var order = _builder.WithDefaultValues();
        order.RecordAuthorization("O-1", "COMPLETED", "A-1", "CREATED", null, null, "USD");
        order.RecordCapture("C-1", "COMPLETED", 3.69m, 0.20m, 3.49m, "USD");

        order.RecordRefund("R-1", "COMPLETED", 1.00m, "USD", "key-1");
        Assert.Equal(OrderStatus.PartiallyRefunded, order.Status);
        Assert.Equal(2.69m, order.RemainingRefundable());

        var replay = order.RecordRefund("R-1", "COMPLETED", 1.00m, "USD", "key-1");
        Assert.Equal("R-1", replay.PayPalRefundId);
        Assert.Equal(2.69m, order.RemainingRefundable());

        order.RecordRefund("R-2", "COMPLETED", 2.69m, "USD", "key-2");
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(0m, order.RemainingRefundable());

        Assert.Throws<PaymentException>(() => order.RecordRefund("R-3", "COMPLETED", 0.01m, "USD", "key-3"));
    }

    [Fact]
    public void DistinctPartialRefundsAreAllowed()
    {
        var order = _builder.WithDefaultValues();
        order.RecordAuthorization("O-1", "COMPLETED", "A-1", "CREATED", null, null, "USD");
        order.RecordCapture("C-1", "COMPLETED", 3.69m, 0.20m, 3.49m, "USD");
        order.RecordRefund("R-1", "COMPLETED", 1.00m, "USD", "a");
        order.RecordRefund("R-2", "COMPLETED", 1.00m, "USD", "b");
        Assert.Equal(2, order.Refunds.Count);
        Assert.Equal(1.69m, order.RemainingRefundable());
    }
}
