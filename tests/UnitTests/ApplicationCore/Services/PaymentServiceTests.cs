using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services;

public class PaymentServiceTests
{
    private readonly IRepository<Payment> _payments = Substitute.For<IRepository<Payment>>();
    private readonly IReadRepository<SavedCard> _cards = Substitute.For<IReadRepository<SavedCard>>();
    private readonly IPayPalGateway _gateway = Substitute.For<IPayPalGateway>();

    private PaymentService Service() => new(_payments, _cards, _gateway);

    private void StorePayment(Payment payment) =>
        _payments.FirstOrDefaultAsync(Arg.Any<PaymentByOrderIdSpecification>(), Arg.Any<CancellationToken>())
            .Returns(payment);

    private static Payment Authorized()
    {
        var p = new Payment(1, "owner@x.com", "USD", 39m, "INV-1");
        p.MarkAuthorized("PP-ORDER", "AUTH-1", "CREATED");
        return p;
    }

    private static Payment Captured()
    {
        var p = Authorized();
        p.MarkCaptured("CAP-1", "COMPLETED", 39m, 1.5m, 37.5m);
        return p;
    }

    [Fact]
    public async Task Pay_WhenAlreadyAuthorized_DoesNotAuthorizeAgain()
    {
        StorePayment(Authorized());

        var result = await Service().PayAsync(1, "owner@x.com", new PaymentInstruction(), CancellationToken.None);

        Assert.Equal(PaymentStatus.Authorized, result.Status);
        await _gateway.DidNotReceiveWithAnyArgs().AuthorizeAsync(default, default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Pay_ForAnotherShoppersOrder_IsNotFound()
    {
        StorePayment(Authorized()); // owned by owner@x.com

        var ex = await Assert.ThrowsAsync<PaymentException>(
            () => Service().PayAsync(1, "attacker@x.com", new PaymentInstruction(), CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task Fulfil_WhenNotYetPaid_IsRejected()
    {
        StorePayment(new Payment(1, "owner@x.com", "USD", 39m, "INV-1")); // AwaitingPayment

        var ex = await Assert.ThrowsAsync<PaymentException>(() => Service().FulfilAsync(1, CancellationToken.None));
        Assert.Equal(409, ex.StatusCode);
        await _gateway.DidNotReceiveWithAnyArgs().CaptureAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task Fulfil_CapturesAndRecordsFeeAndNet()
    {
        StorePayment(Authorized());
        _gateway.CaptureAsync("AUTH-1", 39m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PayPalCaptureResult("CAP-9", "COMPLETED", 39m, 1.2m, 37.8m));

        var result = await Service().FulfilAsync(1, CancellationToken.None);

        Assert.Equal(PaymentStatus.Captured, result.Status);
        Assert.Equal("CAP-9", result.CaptureId);
        Assert.Equal(1.2m, result.PayPalFee);
        Assert.Equal(37.8m, result.NetAmount);
    }

    [Fact]
    public async Task Cancel_VoidsHoldAndReleasesFunds()
    {
        StorePayment(Authorized());

        var result = await Service().CancelAsync(1, CancellationToken.None);

        Assert.Equal(PaymentStatus.Cancelled, result.Status);
        await _gateway.Received(1).VoidAsync("AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refund_RepeatedIdempotencyKey_DoesNotRefundTwice()
    {
        var payment = Captured();
        payment.AddRefund("R-EXIST", 10m, "key-1", "COMPLETED");
        StorePayment(payment);

        var result = await Service().RefundAsync(1, "owner@x.com", 10m, "key-1", CancellationToken.None);

        Assert.Equal("R-EXIST", result.RefundId);
        await _gateway.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task Refund_DistinctKey_CallsGatewayAndRecordsRefund()
    {
        StorePayment(Captured());
        _gateway.RefundAsync("CAP-1", 5m, "key-2", Arg.Any<CancellationToken>())
            .Returns(new PayPalRefundResult("R-NEW", "COMPLETED", 5m));

        var result = await Service().RefundAsync(1, "owner@x.com", 5m, "key-2", CancellationToken.None);

        Assert.Equal("R-NEW", result.RefundId);
        Assert.Equal(5m, result.Amount);
    }

    [Fact]
    public async Task Refund_ExceedingRefundable_IsRejectedBeforeCallingPayPal()
    {
        var payment = Captured();
        payment.AddRefund("R1", 30m, "key-1", "COMPLETED"); // 9.00 remains
        StorePayment(payment);

        var ex = await Assert.ThrowsAsync<PaymentException>(
            () => Service().RefundAsync(1, "owner@x.com", 10m, "key-2", CancellationToken.None));
        Assert.Equal(422, ex.StatusCode);
        await _gateway.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task Refund_ForAnotherShoppersOrder_IsNotFound()
    {
        StorePayment(Captured()); // owned by owner@x.com

        var ex = await Assert.ThrowsAsync<PaymentException>(
            () => Service().RefundAsync(1, "attacker@x.com", 5m, "key-2", CancellationToken.None));
        Assert.Equal(404, ex.StatusCode);
    }
}
