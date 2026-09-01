using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class RefundOrder : PaymentServiceTestBase
{
    private (Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order order, Payment payment) GivenCapturedOrder(decimal amount = 20m)
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        var payment = NewAuthorizedPayment(amount: amount);
        payment.MarkCaptured("CAP-1", amount, 1.10m, amount - 1.10m);
        order.MarkFulfilled();
        GivenOrder(order);
        GivenPayment(payment);
        return (order, payment);
    }

    [Fact]
    public async Task ReplayedIdempotencyKeyReturnsOriginalRefundWithoutChargingAgain()
    {
        var (_, payment) = GivenCapturedOrder();
        Gateway.RefundAsync("CAP-1", OrderId, 5m, "USD", "ret-001", null, Arg.Any<CancellationToken>())
            .Returns(new GatewayRefundResult("REF-1", "COMPLETED", 5m, "USD"));

        var service = CreateService();
        var first = await service.RefundOrderAsync(OrderId, 5m, "ret-001", null, CancellationToken.None);
        var replay = await service.RefundOrderAsync(OrderId, 5m, "ret-001", null, CancellationToken.None);

        Assert.Same(first, replay);
        Assert.Equal("REF-1", replay.PayPalRefundId);
        await Gateway.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(5m, payment.TotalRefunded);
    }

    [Fact]
    public async Task DistinctKeysAllowLegitimatePartialRefunds()
    {
        var (_, payment) = GivenCapturedOrder();
        Gateway.RefundAsync("CAP-1", OrderId, 5m, "USD", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(x => new GatewayRefundResult("REF-" + x.ArgAt<string>(4), "COMPLETED", 5m, "USD"));

        var service = CreateService();
        await service.RefundOrderAsync(OrderId, 5m, "ret-001", null, CancellationToken.None);
        await service.RefundOrderAsync(OrderId, 5m, "ret-002", null, CancellationToken.None);

        Assert.Equal(10m, payment.TotalRefunded);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(10m, payment.RefundableRemaining);
    }

    [Fact]
    public async Task NeverRefundsBeyondWhatWasCaptured()
    {
        GivenCapturedOrder();
        Gateway.RefundAsync("CAP-1", OrderId, 15m, "USD", "ret-001", null, Arg.Any<CancellationToken>())
            .Returns(new GatewayRefundResult("REF-1", "COMPLETED", 15m, "USD"));

        var service = CreateService();
        await service.RefundOrderAsync(OrderId, 15m, "ret-001", null, CancellationToken.None);

        await Assert.ThrowsAsync<OrderStateException>(
            () => service.RefundOrderAsync(OrderId, 6m, "ret-002", null, CancellationToken.None));
    }

    [Fact]
    public async Task FullRefundMarksPaymentRefunded()
    {
        var (_, payment) = GivenCapturedOrder();
        Gateway.RefundAsync("CAP-1", OrderId, 20m, "USD", "ret-001", null, Arg.Any<CancellationToken>())
            .Returns(new GatewayRefundResult("REF-1", "COMPLETED", 20m, "USD"));

        await CreateService().RefundOrderAsync(OrderId, null, "ret-001", null, CancellationToken.None);

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(0m, payment.RefundableRemaining);
    }

    [Fact]
    public async Task RejectsRefundBeforeFulfilment()
    {
        var order = NewOrder();
        order.MarkPaymentAuthorized();
        GivenOrder(order);
        GivenPayment(NewAuthorizedPayment());

        await Assert.ThrowsAsync<OrderStateException>(
            () => CreateService().RefundOrderAsync(OrderId, 5m, "ret-001", null, CancellationToken.None));
    }
}
