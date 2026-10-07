using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

public class OrderPaymentServiceTests : IDisposable
{
    private readonly PaymentTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private Task<PayOrderResult> Pay(int orderId, string buyer = PaymentTestHost.Shopper) =>
        _host.Request(s => s.PayAsync(orderId, buyer, PaymentTestHost.Card, "https://localhost/return"));

    private Task<RefundOrderResult> Refund(int orderId, decimal? amount) =>
        _host.Request(s => s.RefundAsync(orderId, amount, PaymentTestHost.Operator));

    private Task<Order?> Load(int orderId) =>
        _host.Request(s => s.GetOrderWithPaymentsAsync(orderId, CancellationToken.None));

    [Fact]
    public async Task PlaceOrder_UsesCatalogPrices_AndStartsAwaitingPayment()
    {
        var orderId = await _host.PlaceOrder();

        var order = await Load(orderId);
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, order!.PaymentStatus);
        Assert.Equal(51.00m, order.Total());
        Assert.Equal(PaymentTestHost.Shopper, order.BuyerId);
    }

    [Theory]
    [InlineData(999, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -3)]
    public async Task PlaceOrder_RejectsUnknownItemsAndBadQuantities(int catalogItemId, int quantity)
    {
        var result = await _host.Request(s => s.PlaceOrderAsync(PaymentTestHost.Shopper,
            new[] { new OrderLine(catalogItemId, quantity) }, null, CancellationToken.None));

        Assert.Equal(PlaceOrderOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task Pay_Authorised_ChargesExactOrderTotal_AndMarksPaid()
    {
        var orderId = await _host.PlaceOrder();

        var result = await Pay(orderId);

        Assert.Equal(PayOrderOutcome.Paid, result.Outcome);
        var call = Assert.Single(_host.Gateway.PaymentCalls);
        Assert.Equal(5100, call.AmountMinor);
        Assert.Equal("USD", call.Currency);
        Assert.Equal($"ESHOP-{orderId}-PAY-1", call.Reference);

        var order = await Load(orderId);
        Assert.Equal(OrderPaymentStatus.Paid, order!.PaymentStatus);
        Assert.Equal(5100, order.PaidAmountMinor);
        var attempt = Assert.Single(order.PaymentAttempts);
        Assert.Equal(call.IdempotencyKey, attempt.IdempotencyKey);
        Assert.Contains("\"newField\":1", Assert.Single(attempt.ProviderResponses).Body);
    }

    [Fact]
    public async Task Pay_Twice_ChargesOnce()
    {
        var orderId = await _host.PlaceOrder();

        var first = await Pay(orderId);
        var second = await Pay(orderId);

        Assert.Equal(PayOrderOutcome.Paid, first.Outcome);
        Assert.Equal(PayOrderOutcome.AlreadyPaid, second.Outcome);
        Assert.Equal(first.Attempt!.PspReference, second.Attempt!.PspReference);
        Assert.Single(_host.Gateway.PaymentCalls);
    }

    [Fact]
    public async Task Pay_ConcurrentDoubleClick_OnlyOneReachesAdyen()
    {
        var orderId = await _host.PlaceOrder();
        var release = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        _host.Gateway.NextPayment(async r =>
        {
            entered.SetResult();
            await release.Task;
            return ScriptedPaymentGateway.Authorised(r);
        });

        var firstClick = Pay(orderId);
        await entered.Task; // the first request now holds the claim and is waiting on "Adyen"
        var secondClick = await Pay(orderId);
        release.SetResult();
        var first = await firstClick;

        Assert.Equal(PayOrderOutcome.Paid, first.Outcome);
        Assert.Equal(PayOrderOutcome.Busy, secondClick.Outcome);
        Assert.Single(_host.Gateway.PaymentCalls);
    }

    [Fact]
    public async Task Pay_Refused_LeavesOrderUnpaid_TellsShopperWhy_AndAllowsANewAttempt()
    {
        var orderId = await _host.PlaceOrder();
        _host.Gateway.NextPayment(ScriptedPaymentGateway.Refused);

        var refused = await Pay(orderId);

        Assert.Equal(PayOrderOutcome.Refused, refused.Outcome);
        Assert.Contains("declined", refused.Message);
        Assert.Contains("Not enough balance", refused.Message);
        Assert.Contains("No money was taken", refused.Message);
        Assert.Equal(OrderPaymentStatus.AwaitingPayment, (await Load(orderId))!.PaymentStatus);

        var retry = await Pay(orderId);

        Assert.Equal(PayOrderOutcome.Paid, retry.Outcome);
        var calls = _host.Gateway.PaymentCalls.ToArray();
        Assert.Equal(2, calls.Length);
        Assert.NotEqual(calls[0].IdempotencyKey, calls[1].IdempotencyKey);
        Assert.Equal($"ESHOP-{orderId}-PAY-2", calls[1].Reference);
    }

    [Fact]
    public async Task Pay_AnotherShoppersOrder_IsNotFound_AndNeverReachesAdyen()
    {
        var orderId = await _host.PlaceOrder(PaymentTestHost.Shopper);

        var result = await Pay(orderId, PaymentTestHost.OtherShopper);

        Assert.Equal(PayOrderOutcome.NotFound, result.Outcome);
        Assert.Empty(_host.Gateway.PaymentCalls);
    }

    [Fact]
    public async Task Pay_TimedOut_ReportsAdyenDidNotRespond_AndOrderIsPaymentPending()
    {
        var orderId = await _host.PlaceOrder();
        _host.Gateway.NextPayment(ScriptedPaymentGateway.NoAnswer);

        var result = await Pay(orderId);

        Assert.Equal(PayOrderOutcome.ProviderTimeout, result.Outcome);
        Assert.Contains("Adyen did not respond", result.Message);
        Assert.Equal(OrderPaymentStatus.PaymentPending, (await Load(orderId))!.PaymentStatus);
    }

    [Fact]
    public async Task Pay_UnknownAttempt_IsSettledWithSameKeyOnRetry()
    {
        var orderId = await _host.PlaceOrder();
        _host.Gateway.NextPayment(ScriptedPaymentGateway.NoAnswer);
        await Pay(orderId);

        var retry = await Pay(orderId);

        Assert.Equal(PayOrderOutcome.Paid, retry.Outcome);
        var calls = _host.Gateway.PaymentCalls.ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal(calls[0].IdempotencyKey, calls[1].IdempotencyKey);
        Assert.Equal(calls[0].Reference, calls[1].Reference);
        var order = await Load(orderId);
        Assert.Single(order!.PaymentAttempts);
        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
    }

    [Fact]
    public async Task Refund_PartialThenRest_ThenNothingLeft()
    {
        var orderId = await _host.PlaceOrder();
        await Pay(orderId);

        var partial = await Refund(orderId, 5.00m);
        Assert.Equal(RefundOrderOutcome.Received, partial.Outcome);
        Assert.Equal(OrderPaymentStatus.PartiallyRefunded, partial.Order!.PaymentStatus);
        Assert.Equal(4600, partial.Order.RefundableAmountMinor);

        var rest = await Refund(orderId, null);
        Assert.Equal(RefundOrderOutcome.Received, rest.Outcome);
        Assert.Equal(4600, rest.Refund!.AmountMinor);
        Assert.Equal(OrderPaymentStatus.Refunded, rest.Order!.PaymentStatus);

        var more = await Refund(orderId, 0.01m);
        Assert.Equal(RefundOrderOutcome.ExceedsRefundable, more.Outcome);
        Assert.Equal(2, _host.Gateway.RefundCalls.Count);

        var calls = _host.Gateway.RefundCalls.ToArray();
        var payment = (await Load(orderId))!.AuthorisedPayment!;
        Assert.All(calls, c => Assert.Equal(payment.PspReference, c.PaymentPspReference));
    }

    [Fact]
    public async Task Refund_BeyondWhatWasPaid_IsRefusedWithoutCallingAdyen()
    {
        var orderId = await _host.PlaceOrder();
        await Pay(orderId);

        var result = await Refund(orderId, 51.01m);

        Assert.Equal(RefundOrderOutcome.ExceedsRefundable, result.Outcome);
        Assert.Empty(_host.Gateway.RefundCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.001)]
    public async Task Refund_InvalidAmount_IsRefused(decimal amount)
    {
        var orderId = await _host.PlaceOrder();
        await Pay(orderId);

        var result = await Refund(orderId, amount);

        Assert.Equal(RefundOrderOutcome.Invalid, result.Outcome);
        Assert.Empty(_host.Gateway.RefundCalls);
    }

    [Fact]
    public async Task Refund_UnpaidOrder_IsRefused()
    {
        var orderId = await _host.PlaceOrder();

        var result = await Refund(orderId, 1m);

        Assert.Equal(RefundOrderOutcome.NotPaid, result.Outcome);
        Assert.Empty(_host.Gateway.RefundCalls);
    }

    [Fact]
    public async Task Refund_UnknownRefund_IsSettledBeforeNewRefund()
    {
        var orderId = await _host.PlaceOrder();
        await Pay(orderId);
        _host.Gateway.NextRefund(ScriptedPaymentGateway.NoAnswer);

        var timedOut = await Refund(orderId, 10m);
        Assert.Equal(RefundOrderOutcome.ProviderTimeout, timedOut.Outcome);
        // An unconfirmed refund still counts, so the order can never be refunded beyond what was paid.
        Assert.Equal(4100, timedOut.Order!.RefundableAmountMinor);

        var next = await Refund(orderId, 5m);

        Assert.Equal(RefundOrderOutcome.Received, next.Outcome);
        var calls = _host.Gateway.RefundCalls.ToArray();
        Assert.Equal(3, calls.Length);
        Assert.Equal(calls[0].IdempotencyKey, calls[1].IdempotencyKey); // settle re-sends the original
        Assert.Equal(1000, calls[1].AmountMinor);
        Assert.NotEqual(calls[0].IdempotencyKey, calls[2].IdempotencyKey);
        var order = await Load(orderId);
        Assert.All(order!.Refunds, r => Assert.Equal(RefundStatus.Received, r.Status));
        Assert.Equal(1500, order.RefundedAmountMinor);
    }

    [Fact]
    public async Task Refund_StillUnknownAfterSettle_SendsNoNewRefund()
    {
        var orderId = await _host.PlaceOrder();
        await Pay(orderId);
        _host.Gateway.NextRefund(ScriptedPaymentGateway.NoAnswer);
        _host.Gateway.NextRefund(ScriptedPaymentGateway.NoAnswer);
        await Refund(orderId, 10m);

        var result = await Refund(orderId, 5m);

        Assert.Equal(RefundOrderOutcome.PreviousRefundUnsettled, result.Outcome);
        Assert.Equal(2, _host.Gateway.RefundCalls.Count);
        Assert.Single((await Load(orderId))!.Refunds);
    }

    [Fact]
    public async Task ListBuyerOrders_ReturnsOnlyTheCallersOrders()
    {
        var mine = await _host.PlaceOrder(PaymentTestHost.Shopper);
        await _host.PlaceOrder(PaymentTestHost.OtherShopper);
        await Pay(mine);

        var orders = await _host.Request(s => s.ListBuyerOrdersAsync(PaymentTestHost.Shopper, CancellationToken.None));

        var order = Assert.Single(orders);
        Assert.Equal(mine, order.Id);
        Assert.Equal(OrderPaymentStatus.Paid, order.PaymentStatus);
    }
}
