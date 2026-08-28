using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.UnitTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Entities.OrderTests;

public class PaymentLifecycle
{
    [Fact]
    public void NewPaymentStartsAwaitingPaymentAndCarriesStableOperationIds()
    {
        var order = new OrderBuilder().Build();

        order.InitializePayment("usd");

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(PaymentStatus.AwaitingPayment, order.Payment!.Status);
        Assert.Equal("USD", order.Payment.Currency);
        Assert.NotEmpty(order.Payment.CreateOrderRequestId);
        Assert.NotEmpty(order.Payment.AuthorizeRequestId);
        Assert.NotEmpty(order.Payment.ReauthorizeRequestId);
        Assert.NotEmpty(order.Payment.CaptureRequestId);
        Assert.NotEmpty(order.Payment.VoidRequestId);
    }

    [Fact]
    public void CaptureAndRefundsNeverReportMoreThanCaptured()
    {
        var order = new OrderBuilder().Build();
        order.InitializePayment("USD");
        var payment = order.Payment!;
        payment.RecordAuthorization("auth", "CREATED", order.Total(), DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(29));
        order.MarkAuthorized();
        payment.RecordCapture("capture", "COMPLETED", order.Total(), 0.25m,
            order.Total() - 0.25m, DateTimeOffset.UtcNow);
        order.MarkFulfilled();

        var first = payment.AddRefund("one", "request-one", 1m);
        first.RecordResult("refund-one", "COMPLETED", 1m, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var second = payment.AddRefund("two", "request-two", order.Total() - 1m);
        second.RecordResult("refund-two", "COMPLETED", order.Total() - 1m,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        payment.RefreshRefundStatus();
        order.UpdateRefundStatus(payment.CompletedRefundAmount());

        Assert.Equal(order.Total(), payment.CompletedRefundAmount());
        Assert.Equal(order.Total(), payment.ReservedRefundAmount());
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(2, payment.Refunds.Select(x => x.IdempotencyKey).Distinct().Count());
    }
}
