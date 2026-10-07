using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class OrderPayment
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    // Default order: 3 × 1.23 = 3.69
    private static Order PaidOrder(out PaymentAttempt attempt)
    {
        var order = new OrderBuilder().WithDefaultValues();
        attempt = order.StartPaymentAttempt(369, "USD", Now);
        order.RecordPaymentOutcome(attempt, PaymentAttemptStatus.Authorised, "PSP", "Authorised", null, null, null, null,
            Array.Empty<CapturedProviderResponse>(), Now);
        return order;
    }

    [Fact]
    public void NewOrderAwaitsPayment()
    {
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, new OrderBuilder().WithDefaultValues().PaymentStatus);
    }

    [Fact]
    public void InFlightAttemptMeansProcessingAndAuthorisedMeansPaid()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var attempt = order.StartPaymentAttempt(369, "USD", Now);
        Assert.Equal(OrderPaymentStatus.PaymentProcessing, order.PaymentStatus);
        Assert.Equal(PaymentAttemptStatus.InFlight, attempt.Status);

        order.RecordPaymentOutcome(attempt, PaymentAttemptStatus.Authorised, "PSP", "Authorised", null, null, null, null,
            new[] { new CapturedProviderResponse(Now, 200, "{}", null) }, Now);

        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
        Assert.Single(order.ProviderResponses);
    }

    [Fact]
    public void RefusedAttemptReturnsTheOrderToAwaitingPayment()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var attempt = order.StartPaymentAttempt(369, "USD", Now);

        order.RecordPaymentOutcome(attempt, PaymentAttemptStatus.Refused, "PSP", "Refused", "Refused", "2", null, null,
            Array.Empty<CapturedProviderResponse>(), Now);

        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order.PaymentStatus);
    }

    [Fact]
    public void SettlingAnUnknownAttemptReusesItsIdempotencyKeyAndReference()
    {
        var order = new OrderBuilder().WithDefaultValues();
        var unknown = order.StartPaymentAttempt(369, "USD", Now);
        order.RecordPaymentOutcome(unknown, PaymentAttemptStatus.Unknown, null, null, null, null, null, null,
            Array.Empty<CapturedProviderResponse>(), Now);

        var settle = order.StartPaymentAttempt(369, "USD", Now, settles: unknown);

        Assert.Equal(unknown.IdempotencyKey, settle.IdempotencyKey);
        Assert.Equal(unknown.Reference, settle.Reference);
        Assert.Equal(2, settle.AttemptNumber);
        Assert.Equal(PaymentAttemptStatus.Superseded, unknown.Status);
    }

    [Fact]
    public void APaidOrderCannotStartAnotherPayment()
    {
        var order = PaidOrder(out _);

        Assert.Throws<PaymentStateException>(() => order.StartPaymentAttempt(369, "USD", Now));
    }

    [Fact]
    public void RefundsCannotExceedWhatWasPaid()
    {
        var order = PaidOrder(out _);
        order.StartRefund(2.00m, 200, RefundReason.Return, null, Now);

        Assert.Equal(169, order.RefundableMinorUnits);
        Assert.Throws<PaymentStateException>(() => order.StartRefund(1.70m, 170, null, null, Now));
    }

    [Fact]
    public void RejectedRefundReleasesItsAmountAndReceivedRefundsSetTheStatus()
    {
        var order = PaidOrder(out _);
        var rejected = order.StartRefund(3.69m, 369, null, null, Now);
        order.RecordRefundOutcome(rejected, RefundStatus.Rejected, null, "167", "no", Array.Empty<CapturedProviderResponse>(), Now);
        Assert.Equal(369, order.RefundableMinorUnits);
        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);

        var partial = order.StartRefund(1.00m, 100, null, null, Now);
        order.RecordRefundOutcome(partial, RefundStatus.Received, "R1", null, null, Array.Empty<CapturedProviderResponse>(), Now);
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, order.PaymentStatus);

        var rest = order.StartRefund(2.69m, 269, null, null, Now);
        order.RecordRefundOutcome(rest, RefundStatus.Received, "R2", null, null, Array.Empty<CapturedProviderResponse>(), Now);
        Assert.Equal(OrderPaymentStatus.Refunded, order.PaymentStatus);
        Assert.Equal(0, order.RefundableMinorUnits);
    }

    [Fact]
    public void AnUnknownRefundKeepsItsAmountReserved()
    {
        var order = PaidOrder(out _);
        var refund = order.StartRefund(3.00m, 300, null, null, Now);
        order.RecordRefundOutcome(refund, RefundStatus.Unknown, null, null, null, Array.Empty<CapturedProviderResponse>(), Now);

        Assert.Equal(69, order.RefundableMinorUnits);
        Assert.True(refund.NeedsSettlement(Now, TimeSpan.FromMinutes(2)));
    }
}
