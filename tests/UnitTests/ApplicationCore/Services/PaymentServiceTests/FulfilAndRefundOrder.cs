using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class FulfilAndRefundOrder
{
    private readonly PaymentServiceFixture _fixture = new();

    [Fact]
    public async Task FulfilmentCapturesTheHoldAndRecordsTheFeeAndNetProceeds()
    {
        var order = _fixture.GivenOrder(OrderLifecycleStatus.Authorized);
        _fixture.GivenPayment(PaymentServiceFixture.AuthorizedPayment(order.Total()));

        _fixture.Gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("PP-CAPTURE", "COMPLETED", order.Total(), "USD", 0.41m, order.Total() - 0.41m));

        var payment = await _fixture.Build().FulfilAsync(1, default);

        Assert.Equal("PP-CAPTURE", payment.CaptureId);
        Assert.Equal(order.Total(), payment.CapturedAmount);
        Assert.Equal(0.41m, payment.PayPalFee);
        Assert.Equal(order.Total() - 0.41m, payment.NetAmount);
        Assert.Equal(OrderLifecycleStatus.Fulfilled, order.Status);
    }

    [Fact]
    public async Task AStaleHoldIsRenewedBeforeTheMoneyIsTaken()
    {
        var order = _fixture.GivenOrder(OrderLifecycleStatus.Authorized);
        // Expired yesterday, so fulfilment must renew rather than fail.
        _fixture.GivenPayment(PaymentServiceFixture.AuthorizedPayment(
            order.Total(), DateTimeOffset.UtcNow.AddDays(-1).ToString("O")));

        _fixture.Gateway.ReauthorizeAsync("PP-AUTH-1", order.Total(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationSnapshot("PP-AUTH-2", "CREATED", order.Total(), "USD",
                DateTimeOffset.UtcNow.AddDays(3)));

        _fixture.Gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CaptureResult("PP-CAPTURE", "COMPLETED", order.Total(), "USD", 0.41m, order.Total() - 0.41m));

        var payment = await _fixture.Build().FulfilAsync(1, default);

        // The capture must go against the replacement hold, not the stale one.
        await _fixture.Gateway.Received(1).CaptureAsync("PP-AUTH-2", order.Total(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal("PP-AUTH-2", payment.AuthorizationId);
        Assert.Equal(OrderLifecycleStatus.Fulfilled, order.Status);
    }

    [Fact]
    public async Task AHoldThatCanNoLongerBeRenewedFailsWithSomethingAnOperatorCanActOn()
    {
        _fixture.GivenOrder(OrderLifecycleStatus.Authorized);
        _fixture.GivenPayment(PaymentServiceFixture.AuthorizedPayment(
            3.69m, DateTimeOffset.UtcNow.AddDays(-40).ToString("O")));

        _fixture.Gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<AuthorizationSnapshot>(_ => throw new PaymentGatewayException(
                "authorization has expired", PaymentGatewayFailure.Conflict));

        var failure = await Assert.ThrowsAsync<OrderStateException>(() => _fixture.Build().FulfilAsync(1, default));

        // The operator is told what to do next, not just that it failed.
        Assert.Contains("/pay", failure.Message);
        Assert.Contains("no money has been taken", failure.Message, StringComparison.OrdinalIgnoreCase);

        await _fixture.Gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARefusedCaptureRenewsTheHoldWhenTheProcessorSaysItIsNoLongerCapturable()
    {
        var order = _fixture.GivenOrder(OrderLifecycleStatus.Authorized);
        _fixture.GivenPayment(PaymentServiceFixture.AuthorizedPayment(order.Total()));

        var attempts = 0;
        _fixture.Gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++attempts == 1
                ? throw new PaymentGatewayException("cannot capture", PaymentGatewayFailure.Conflict)
                : Task.FromResult(new CaptureResult("PP-CAPTURE", "COMPLETED", order.Total(), "USD", 0.41m, 3.28m)));

        // A status outside the declared capturable set — this is the defensive read, not a guess at
        // a provider error string.
        _fixture.Gateway.GetAuthorizationAsync("PP-AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new AuthorizationSnapshot("PP-AUTH-1", "EXPIRED", order.Total(), "USD", null));

        _fixture.Gateway.ReauthorizeAsync("PP-AUTH-1", order.Total(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationSnapshot("PP-AUTH-2", "CREATED", order.Total(), "USD",
                DateTimeOffset.UtcNow.AddDays(3)));

        var payment = await _fixture.Build().FulfilAsync(1, default);

        Assert.Equal("PP-CAPTURE", payment.CaptureId);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ARefusedCaptureIsNotRetriedWhenTheHoldIsStillPerfectlyCapturable()
    {
        _fixture.GivenOrder(OrderLifecycleStatus.Authorized);
        _fixture.GivenPayment(PaymentServiceFixture.AuthorizedPayment());

        _fixture.Gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<CaptureResult>(_ => throw new PaymentGatewayException(
                "insufficient funds", PaymentGatewayFailure.Conflict));

        _fixture.Gateway.GetAuthorizationAsync("PP-AUTH-1", Arg.Any<CancellationToken>())
            .Returns(new AuthorizationSnapshot("PP-AUTH-1", "CREATED", 3.69m, "USD", null));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => _fixture.Build().FulfilAsync(1, default));

        // Renewing a perfectly good hold would just place a second one.
        await _fixture.Gateway.DidNotReceive().ReauthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfillingAnAlreadyCapturedOrderReturnsTheExistingCapture()
    {
        _fixture.GivenOrder(OrderLifecycleStatus.Fulfilled);
        _fixture.GivenPayment(PaymentServiceFixture.CapturedPayment());

        var payment = await _fixture.Build().FulfilAsync(1, default);

        Assert.Equal("PP-CAPTURE", payment.CaptureId);
        await _fixture.Gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARepeatedRefundKeyReturnsTheFirstRefundWithoutCallingTheProcessor()
    {
        _fixture.GivenOrder(OrderLifecycleStatus.Fulfilled);
        var payment = PaymentServiceFixture.CapturedPayment();
        payment.AddRefund("caller-key", "PP-REFUND-1", "COMPLETED", 1m);
        _fixture.GivenPayment(payment);

        var refund = await _fixture.Build()
            .RefundAsync(PaymentServiceFixture.BuyerId, 1, 1m, "caller-key", default);

        Assert.Equal("PP-REFUND-1", refund.RefundId);
        await _fixture.Gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFirstWholeBalanceRefundIsSentAsAFullRefund()
    {
        _fixture.GivenOrder(OrderLifecycleStatus.Fulfilled);
        _fixture.GivenPayment(PaymentServiceFixture.CapturedPayment(10m));

        _fixture.Gateway.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new RefundResult("PP-REFUND-1", "COMPLETED", 10m, "USD"));

        await _fixture.Build().RefundAsync(PaymentServiceFixture.BuyerId, 1, null, "k", default);

        // A null amount is what the processor documents as "refund the whole capture".
        await _fixture.Gateway.Received(1).RefundAsync("PP-CAPTURE", null, Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundingTheRemainderAfterAPartialRefundNamesTheAmount()
    {
        _fixture.GivenOrder(OrderLifecycleStatus.Fulfilled);
        var payment = PaymentServiceFixture.CapturedPayment(10m);
        payment.AddRefund("first", "PP-REFUND-1", "COMPLETED", 4m);
        _fixture.GivenPayment(payment);

        _fixture.Gateway.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new RefundResult("PP-REFUND-2", "COMPLETED", 6m, "USD"));

        await _fixture.Build().RefundAsync(PaymentServiceFixture.BuyerId, 1, null, "second", default);

        // An empty body here would refund the whole capture a second time.
        await _fixture.Gateway.Received(1).RefundAsync("PP-CAPTURE", 6m, Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARefundBeyondTheCapturedAmountNeverReachesTheProcessor()
    {
        _fixture.GivenOrder(OrderLifecycleStatus.Fulfilled);
        var payment = PaymentServiceFixture.CapturedPayment(10m);
        payment.AddRefund("first", "PP-REFUND-1", "COMPLETED", 8m);
        _fixture.GivenPayment(payment);

        await Assert.ThrowsAsync<PaymentValidationException>(() => _fixture.Build()
            .RefundAsync(PaymentServiceFixture.BuyerId, 1, 5m, "second", default));

        await _fixture.Gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellingBeforeFulfilmentReleasesTheHoldSoNoMoneyEverMoved()
    {
        var order = _fixture.GivenOrder(OrderLifecycleStatus.Authorized);
        _fixture.GivenPayment(PaymentServiceFixture.AuthorizedPayment());

        var payment = await _fixture.Build().CancelAsync(1, default);

        await _fixture.Gateway.Received(1).VoidAsync("PP-AUTH-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal("Voided", payment!.PaymentStatus);
        Assert.Equal(OrderLifecycleStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task AFulfilledOrderCannotBeCancelled()
    {
        _fixture.GivenOrder(OrderLifecycleStatus.Fulfilled);
        _fixture.GivenPayment(PaymentServiceFixture.CapturedPayment());

        var failure = await Assert.ThrowsAsync<OrderStateException>(() => _fixture.Build().CancelAsync(1, default));

        Assert.Contains("refunds", failure.Message);
        await _fixture.Gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
