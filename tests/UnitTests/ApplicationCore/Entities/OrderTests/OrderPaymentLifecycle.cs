using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentLifecycle
{
    [Fact]
    public void AuthorizationIsIdempotentOnceHeld()
    {
        var order = AwaitingPaymentOrder();
        RecordAuth(order, "AUTH-1");
        RecordAuth(order, "AUTH-1");

        Assert.Equal(OrderLifecycleStatus.Authorized, order.Status);
        Assert.Equal("AUTH-1", order.Payment!.AuthorizationId);
    }

    [Fact]
    public void CaptureRecordsFeeAndNetProceeds()
    {
        var order = AwaitingPaymentOrder();
        RecordAuth(order, "AUTH-1");
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m);

        Assert.Equal(OrderLifecycleStatus.Fulfilled, order.Status);
        Assert.Equal(3.69m, order.Payment!.CapturedAmount);
        Assert.Equal(0.41m, order.Payment.PaypalFee);
        Assert.Equal(3.28m, order.Payment.NetAmount);
    }

    [Fact]
    public void DuplicateRefundIdempotencyKeyDoesNotRefundTwice()
    {
        var order = FulfilledOrder();
        var first = order.RecordRefund("R-1", "COMPLETED", 1.00m, "key-a");
        var second = order.RecordRefund("R-1", "COMPLETED", 1.00m, "key-a");

        Assert.Same(first, second);
        Assert.Equal(1.00m, order.Payment!.TotalRefunded);
        Assert.Equal(OrderLifecycleStatus.PartiallyRefunded, order.Status);
    }

    [Fact]
    public void DistinctPartialRefundsAreAllowedUntilCapturedAmount()
    {
        var order = FulfilledOrder();
        order.RecordRefund("R-1", "COMPLETED", 1.00m, "key-a");
        order.RecordRefund("R-2", "COMPLETED", 2.69m, "key-b");

        Assert.Equal(OrderLifecycleStatus.Refunded, order.Status);
        Assert.Equal(0m, order.Payment!.RefundableRemaining);
    }

    [Fact]
    public void RefundCannotExceedCapturedAmount()
    {
        var order = FulfilledOrder();
        Assert.Throws<OrderPaymentException>(() => order.RecordRefund("R-1", "COMPLETED", 99m, "key-a"));
    }

    [Fact]
    public void CancelAfterFulfilmentIsRejected()
    {
        var order = FulfilledOrder();
        Assert.Throws<OrderPaymentException>(order.EnsureCanCancel);
    }

    [Fact]
    public void VoidedAuthorizationCancelsTheOrder()
    {
        var order = AwaitingPaymentOrder();
        RecordAuth(order, "AUTH-1");
        order.RecordVoid("VOIDED");

        Assert.Equal(OrderLifecycleStatus.Cancelled, order.Status);
        Assert.Equal("VOIDED", order.Payment!.AuthorizationStatus);
    }

    [Fact]
    public void StaleAuthorizationPastTwentyNineDaysCannotBeRenewed()
    {
        var order = AwaitingPaymentOrder();
        order.RecordAuthorization(
            "PAYPAL-ORDER",
            "AUTH-1",
            "CREATED",
            "USD",
            DateTimeOffset.UtcNow.AddDays(-30),
            DateTimeOffset.UtcNow.AddDays(-1));

        Assert.True(order.AuthorizationCanNoLongerBeRenewed(DateTimeOffset.UtcNow));
        Assert.True(order.AuthorizationHonorPeriodElapsed(DateTimeOffset.UtcNow));
    }

    private static Order AwaitingPaymentOrder()
    {
        var builder = new OrderBuilder();
        return new Order(
            builder.TestBuyerId,
            new AddressBuilder().WithDefaultValues(),
            new List<OrderItem> { new OrderItem(builder.TestCatalogItemOrdered, 1.23m, 3) },
            OrderLifecycleStatus.AwaitingPayment);
    }

    private static Order FulfilledOrder()
    {
        var order = AwaitingPaymentOrder();
        RecordAuth(order, "AUTH-1");
        order.RecordCapture("CAP-1", "COMPLETED", 3.69m, 0.41m, 3.28m);
        return order;
    }

    private static void RecordAuth(Order order, string authorizationId)
    {
        order.RecordAuthorization(
            "PAYPAL-ORDER",
            authorizationId,
            "CREATED",
            "USD",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(29));
    }
}
