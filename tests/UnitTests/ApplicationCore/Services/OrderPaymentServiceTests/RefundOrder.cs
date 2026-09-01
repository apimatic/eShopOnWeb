using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderPaymentServiceTests;

public class RefundOrder : OrderPaymentServiceTestBase
{
    [Fact]
    public async Task PartialRefundReducesRemainingRefundable()
    {
        var order = NewCapturedOrder();
        ReturnsOrder(order);
        Gateway.RefundCaptureAsync("CAP-1", 10m, "USD", "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("RF-1", "COMPLETED", 10m, "USD", 10m));

        var outcome = await CreateService().RefundOrderAsync(1, 10m, "key-1");

        Assert.False(outcome.Replayed);
        Assert.Equal("RF-1", outcome.Refund.PayPalRefundId);
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, outcome.Order.PaymentStatus);
        Assert.Equal(10m, outcome.Order.TotalRefunded);
        Assert.Equal(14m, outcome.Order.RemainingRefundable);
    }

    [Fact]
    public async Task SameIdempotencyKeyReturnsOriginalRefundWithoutCallingGateway()
    {
        var order = NewCapturedOrder();
        order.AddRefund("RF-1", 10m, "USD", "COMPLETED", "key-1");
        ReturnsOrder(order);

        var outcome = await CreateService().RefundOrderAsync(1, 10m, "key-1");

        Assert.True(outcome.Replayed);
        Assert.Equal("RF-1", outcome.Refund.PayPalRefundId);
        await Gateway.DidNotReceive().RefundCaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundBeyondCapturedAmountIsRejected()
    {
        var order = NewCapturedOrder();
        order.AddRefund("RF-1", 10m, "USD", "COMPLETED", "key-1");
        ReturnsOrder(order);

        var ex = await Assert.ThrowsAsync<PaymentStateException>(
            () => CreateService().RefundOrderAsync(1, 15m, "key-2"));

        Assert.Contains("remaining refundable amount", ex.Message);
        await Gateway.DidNotReceive().RefundCaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OmittedAmountRefundsTheFullRemainder()
    {
        var order = NewCapturedOrder();
        ReturnsOrder(order);
        Gateway.RefundCaptureAsync("CAP-1", 24m, "USD", "key-1", Arg.Any<CancellationToken>())
            .Returns(new RefundResult("RF-1", "COMPLETED", 24m, "USD", 24m));

        var outcome = await CreateService().RefundOrderAsync(1, null, "key-1");

        Assert.Equal(OrderPaymentStatus.Refunded, outcome.Order.PaymentStatus);
        Assert.Equal(0m, outcome.Order.RemainingRefundable);
    }

    [Fact]
    public async Task RefundBeforeCaptureConflicts()
    {
        ReturnsOrder(NewAuthorizedOrder());

        await Assert.ThrowsAsync<PaymentStateException>(() => CreateService().RefundOrderAsync(1, 5m, "key-1"));
    }
}
