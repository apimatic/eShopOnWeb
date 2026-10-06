using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPaymentStatusTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    // Attempts and refunds reach the order through EF navigation fix-up; tests add them the same way.
    private static void Attach<T>(Order order, string field, T child) =>
        ((List<T>)typeof(Order).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(order)!).Add(child);

    private static OrderPaymentAttempt Attempt(PaymentAttemptStatus status, long amount = 1000)
    {
        var attempt = new OrderPaymentAttempt(1, 1, amount, "USD", Now);
        attempt.Record(status, new PaymentProviderResult { Outcome = PaymentProviderOutcome.Authorised, PspReference = "PSP" }, Now);
        return attempt;
    }

    private static OrderRefund Refund(int sequence, long amount, RefundStatus status)
    {
        var refund = new OrderRefund(1, sequence, "PSP", amount, "USD", null, "admin", Now);
        refund.Record(status, new RefundProviderResult { Outcome = RefundProviderOutcome.Received }, Now);
        return refund;
    }

    [Fact]
    public void NewOrderAwaitsPayment()
    {
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, new OrderBuilder().WithDefaultValues().PaymentStatus);
    }

    [Theory]
    [InlineData(PaymentAttemptStatus.InFlight, OrderPaymentStatus.PaymentPending)]
    [InlineData(PaymentAttemptStatus.Unknown, OrderPaymentStatus.PaymentPending)]
    [InlineData(PaymentAttemptStatus.Pending, OrderPaymentStatus.PaymentPending)]
    [InlineData(PaymentAttemptStatus.Refused, OrderPaymentStatus.AwaitingPayment)]
    [InlineData(PaymentAttemptStatus.Rejected, OrderPaymentStatus.AwaitingPayment)]
    [InlineData(PaymentAttemptStatus.ActionRequired, OrderPaymentStatus.AwaitingPayment)]
    [InlineData(PaymentAttemptStatus.Authorised, OrderPaymentStatus.Paid)]
    public void FollowsTheAttempts(PaymentAttemptStatus attemptStatus, OrderPaymentStatus expected)
    {
        var order = new OrderBuilder().WithDefaultValues();
        Attach(order, "_paymentAttempts", Attempt(attemptStatus));

        Assert.Equal(expected, order.PaymentStatus);
    }

    [Fact]
    public void OnlyAcceptedRefundsCountAsRefunded()
    {
        var order = new OrderBuilder().WithDefaultValues();
        Attach(order, "_paymentAttempts", Attempt(PaymentAttemptStatus.Authorised, 1000));
        Attach(order, "_refunds", Refund(1, 400, RefundStatus.Received));
        Attach(order, "_refunds", Refund(2, 600, RefundStatus.Failed));

        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);
        Assert.Equal(400, order.RefundedMinorUnits);

        Attach(order, "_refunds", Refund(3, 600, RefundStatus.Received));
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
    }
}
