using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PaymentTests;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

public class PaymentServiceTests : IDisposable
{
    private readonly PaymentTestHost _host = new();

    public void Dispose() => _host.Dispose();

    internal static CardDetails TestCard(string number = "4111111111111111") =>
        new(number, "2030-12", "123", "Test Shopper", new CardBillingAddress("1 Main St", null, "San Jose", "CA", "95131", "US"));

    private Task<OrderPayment> PayAsync(int orderId, string buyer = PaymentTestHost.Buyer, CardDetails? card = null, int? methodId = null) =>
        _host.InScopeAsync(sp => sp.GetRequiredService<PaymentService>()
            .PayAsync(orderId, buyer, new PayOrderCommand(methodId is null ? card ?? TestCard() : null, methodId), CancellationToken.None));

    private Task<OrderPayment> FulfilAsync(int orderId) =>
        _host.InScopeAsync(sp => sp.GetRequiredService<PaymentService>().FulfilAsync(orderId, CancellationToken.None));

    private Task<(OrderPayment Payment, PaymentRefund Refund)> RefundAsync(int orderId, decimal? amount, string key, string buyer = PaymentTestHost.Buyer) =>
        _host.InScopeAsync(sp => sp.GetRequiredService<PaymentService>().RefundAsync(orderId, buyer, amount, key, CancellationToken.None));

    private Task<(Order Order, OrderPayment? Payment)> LoadAsync(int orderId) =>
        _host.InScopeAsync(sp => sp.GetRequiredService<PaymentService>().GetOrderWithPaymentAsync(orderId, CancellationToken.None));

    private static Func<HttpRequestMessage, string?, CancellationToken, Task<HttpResponseMessage?>> FailOnce(string method, string pathPattern, Func<HttpResponseMessage> failure)
    {
        var failed = 0;
        return (req, _, _) =>
        {
            if (req.Method.Method == method && System.Text.RegularExpressions.Regex.IsMatch(req.RequestUri!.AbsolutePath, pathPattern)
                && Interlocked.Exchange(ref failed, 1) == 0)
            {
                return Task.FromResult<HttpResponseMessage?>(failure());
            }
            return Task.FromResult<HttpResponseMessage?>(null);
        };
    }

    /// <summary>Lets the request reach the fake (so PayPal "acts"), then drops the connection before the answer arrives.</summary>
    private Func<HttpRequestMessage, string?, CancellationToken, Task<HttpResponseMessage?>> ActThenDropOnce(string method, string pathPattern)
    {
        var dropped = 0;
        return async (req, body, ct) =>
        {
            if (req.Method.Method == method && System.Text.RegularExpressions.Regex.IsMatch(req.RequestUri!.AbsolutePath, pathPattern)
                && Interlocked.Exchange(ref dropped, 1) == 0)
            {
                await ForwardAsync(_host.PayPal, req, body, ct); // PayPal processes it…
                throw new HttpRequestException("connection reset"); // …but the answer is lost
            }
            return null;
        };
    }

    // ───────────── authorize ─────────────

    [Fact]
    public async Task Pay_AuthorizesExactOrderTotal_WithoutCardDataOnTheOrderCreate()
    {
        var orderId = await _host.PlaceOrderAsync();

        var payment = await PayAsync(orderId);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(51.00m, payment.Amount);
        Assert.Equal("VISA", payment.CardBrand);
        Assert.Equal("1111", payment.CardLastDigits);
        Assert.Equal(OrderStatus.PaymentAuthorized, (await LoadAsync(orderId)).Order.Status);

        var create = _host.PayPal.Requests.Single(r => r.Method == "POST" && r.Path == "/v2/checkout/orders");
        var body = JsonNode.Parse(create.Body!)!;
        Assert.Equal("AUTHORIZE", (string)body["intent"]!);
        Assert.Equal("51.00", (string)body["purchase_units"]![0]!["amount"]!["value"]!);
        Assert.Equal("USD", (string)body["purchase_units"]![0]!["amount"]!["currency_code"]!);
        Assert.DoesNotContain("4111111111111111", create.Body);
        Assert.False(string.IsNullOrEmpty(create.PayPalRequestId));
        Assert.Equal("return=representation", create.Prefer);
        var authorize = _host.PayPal.Requests.Single(r => r.Path.EndsWith("/authorize"));
        Assert.False(string.IsNullOrEmpty(authorize.PayPalRequestId));
        Assert.NotEqual(create.PayPalRequestId, authorize.PayPalRequestId);
    }

    [Fact]
    public async Task Pay_Twice_DoesNotAuthorizeTwice()
    {
        var orderId = await _host.PlaceOrderAsync();

        var first = await PayAsync(orderId);
        var second = await PayAsync(orderId);

        Assert.Equal(first.AuthorizationId, second.AuthorizationId);
        Assert.Equal(1, _host.PayPal.Count("POST", "/authorize$"));
    }

    [Fact]
    public async Task Pay_ConcurrentDoubleClick_OnlyOneReachesPayPal()
    {
        var orderId = await _host.PlaceOrderAsync();
        var gate = new SemaphoreSlim(0);
        _host.PayPal.Intercept = async (req, _, ct) =>
        {
            if (req.RequestUri!.AbsolutePath == "/v2/checkout/orders") await gate.WaitAsync(ct); // hold the first caller inside PayPal
            return null;
        };

        var first = PayAsync(orderId);
        await Task.Delay(300);
        var second = await Assert.ThrowsAsync<PaymentRequestException>(() => PayAsync(orderId));
        gate.Release();
        await first;

        Assert.Equal(PaymentErrorKind.InProgress, second.Kind);
        Assert.Equal(1, _host.PayPal.Count("POST", "^/v2/checkout/orders$"));
        Assert.Equal(1, _host.PayPal.Count("POST", "/authorize$"));
    }

    [Fact]
    public async Task Pay_Declined_RecordsFailure_AndAllowsANewAttempt()
    {
        var orderId = await _host.PlaceOrderAsync();

        var ex = await Assert.ThrowsAsync<PaymentGatewayException>(() => PayAsync(orderId, card: TestCard(FakePayPal.DeclinedCardNumber)));
        Assert.Equal(PaymentGatewayFailure.Rejected, ex.Failure);
        Assert.Equal("INSTRUMENT_DECLINED", ex.ProviderIssue);
        Assert.Equal("fake-debug-422", ex.DebugId);
        var (order, failed) = await LoadAsync(orderId);
        Assert.Equal(PaymentStatus.AuthorizationFailed, failed!.Status);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);

        var retried = await PayAsync(orderId);
        Assert.Equal(PaymentStatus.Authorized, retried.Status);
        Assert.Equal(2, retried.Attempt);
    }

    [Fact]
    public async Task Pay_PayerActionRequired_IsReportedNotRoundTripped()
    {
        var orderId = await _host.PlaceOrderAsync();

        var ex = await Assert.ThrowsAsync<PaymentRequestException>(() => PayAsync(orderId, card: TestCard(FakePayPal.ChallengeCardNumber)));

        Assert.Equal("payer_action_required", ex.Code);
        Assert.Equal(PaymentStatus.AuthorizationFailed, (await LoadAsync(orderId)).Payment!.Status);
    }

    [Fact]
    public async Task Pay_CreateOrderConnectionFails_ResendsWithSameRequestId()
    {
        var orderId = await _host.PlaceOrderAsync();
        _host.PayPal.Intercept = ActThenDropOnce("POST", "^/v2/checkout/orders$");

        var payment = await PayAsync(orderId);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        var creates = _host.PayPal.Requests.Where(r => r.Method == "POST" && r.Path == "/v2/checkout/orders").ToList();
        Assert.True(creates.Count >= 2);
        Assert.Single(creates.Select(c => c.PayPalRequestId).Distinct());
    }

    [Fact]
    public async Task Pay_AuthorizeConnectionFails_SettledFromGetOrder()
    {
        var orderId = await _host.PlaceOrderAsync();
        _host.PayPal.Intercept = ActThenDropOnce("POST", "/authorize$");

        var payment = await PayAsync(orderId);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(1, _host.PayPal.Count("GET", "^/v2/checkout/orders/[A-Z0-9]+$"));
        Assert.Equal(1, _host.PayPal.Count("POST", "/authorize$")); // the hold landed once; it was read back, not re-placed
    }

    [Fact]
    public async Task Pay_ProviderSilent_ReturnsPending_AndRepeatSettlesWithoutSecondHold()
    {
        var orderId = await _host.PlaceOrderAsync();
        var hang = 1;
        _host.PayPal.Intercept = async (req, body, ct) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/authorize") && Interlocked.Exchange(ref hang, 0) == 1)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            return null;
        };

        var pending = await Assert.ThrowsAsync<PaymentOutcomePendingException>(() => PayAsync(orderId));
        Assert.Equal(PaymentGatewayFailure.Timeout, pending.Cause.Failure);
        Assert.NotNull((await LoadAsync(orderId)).Payment!.OutcomeUnknownSince);

        var settled = await PayAsync(orderId);
        Assert.Equal(PaymentStatus.Authorized, settled.Status);
        Assert.Null(settled.OutcomeUnknownSince);
        Assert.Single(_host.PayPal.Requests.Where(r => r.Path.EndsWith("/authorize")).Select(r => r.PayPalRequestId).Distinct());
    }

    [Fact]
    public async Task Pay_WithAnotherShoppersOrder_IsNotFound()
    {
        var orderId = await _host.PlaceOrderAsync();

        var ex = await Assert.ThrowsAsync<PaymentRequestException>(() => PayAsync(orderId, PaymentTestHost.OtherBuyer));

        Assert.Equal(PaymentErrorKind.NotFound, ex.Kind);
        Assert.Empty(_host.PayPal.Requests);
    }

    [Fact]
    public async Task Pay_WithAnotherShoppersSavedCard_IsNotFound()
    {
        var method = await _host.InScopeAsync(sp => sp.GetRequiredService<PaymentMethodService>().SaveAsync(PaymentTestHost.Buyer, TestCard(), CancellationToken.None));
        var otherOrder = await _host.PlaceOrderAsync(PaymentTestHost.OtherBuyer);

        var ex = await Assert.ThrowsAsync<PaymentRequestException>(() => PayAsync(otherOrder, PaymentTestHost.OtherBuyer, methodId: method.Id));

        Assert.Equal(PaymentErrorKind.NotFound, ex.Kind);
        Assert.Equal(0, _host.PayPal.Count("POST", "/authorize$"));
    }

    [Fact]
    public async Task Pay_WithSavedCard_SendsOnlyTheVaultId()
    {
        var method = await _host.InScopeAsync(sp => sp.GetRequiredService<PaymentMethodService>().SaveAsync(PaymentTestHost.Buyer, TestCard(), CancellationToken.None));
        var orderId = await _host.PlaceOrderAsync();

        var payment = await PayAsync(orderId, methodId: method.Id);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(method.Id, payment.SavedPaymentMethodId);
        var authorize = _host.PayPal.Requests.Single(r => r.Path.EndsWith("/authorize"));
        Assert.Equal(method.PayPalVaultId, (string)JsonNode.Parse(authorize.Body!)!["payment_source"]!["card"]!["vault_id"]!);
        Assert.DoesNotContain("4111", authorize.Body);
    }

    // ───────────── fulfil / capture ─────────────

    [Fact]
    public async Task Fulfil_CapturesAndRecordsWhatPayPalReported()
    {
        var orderId = await _host.PlaceOrderAsync();
        await PayAsync(orderId);

        var payment = await FulfilAsync(orderId);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Equal(51.00m, payment.CapturedAmount);
        Assert.Equal(2.27m, payment.PayPalFee);   // fake fee: 3.49% + 0.49
        Assert.Equal(48.73m, payment.NetAmount);
        Assert.Equal(OrderStatus.Fulfilled, (await LoadAsync(orderId)).Order.Status);

        var again = await FulfilAsync(orderId);
        Assert.Equal(payment.CaptureId, again.CaptureId);
        Assert.Equal(1, _host.PayPal.Count("POST", "/capture$"));
    }

    [Fact]
    public async Task Fulfil_AfterHonorPeriod_ReauthorizesThenCaptures()
    {
        var orderId = await _host.PlaceOrderAsync();
        var original = await PayAsync(orderId);
        _host.Clock.Advance(TimeSpan.FromDays(4));

        var payment = await FulfilAsync(orderId);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.NotEqual(original.AuthorizationId, payment.AuthorizationId);
        Assert.Equal(1, _host.PayPal.Count("POST", "/reauthorize$"));
        var capture = _host.PayPal.Requests.Single(r => r.Path.EndsWith("/capture"));
        Assert.Contains(payment.AuthorizationId!, capture.Path);
    }

    [Fact]
    public async Task Fulfil_AuthorizationExpired_ReportsActionableError()
    {
        var orderId = await _host.PlaceOrderAsync();
        await PayAsync(orderId);
        _host.Clock.Advance(TimeSpan.FromDays(30));

        var ex = await Assert.ThrowsAsync<PaymentRequestException>(() => FulfilAsync(orderId));

        Assert.Equal("authorization_expired", ex.Code);
        Assert.Contains("Cancel this order", ex.Message);
        Assert.Equal(0, _host.PayPal.Count("POST", "/(capture|reauthorize)$"));
        Assert.Equal(PaymentStatus.Authorized, (await LoadAsync(orderId)).Payment!.Status);
    }

    [Fact]
    public async Task Fulfil_ReauthorizeRefused_ReportsActionableError()
    {
        var orderId = await _host.PlaceOrderAsync();
        await PayAsync(orderId);
        _host.Clock.Advance(TimeSpan.FromDays(5));
        _host.PayPal.Intercept = FailOnce("POST", "/reauthorize$", () => FakePayPal.JsonResponse(HttpStatusCode.UnprocessableEntity,
            """{"name":"UNPROCESSABLE_ENTITY","message":"Semantically incorrect.","debug_id":"dbg-reauth","details":[{"issue":"REAUTHORIZATION_NOT_ALLOWED","description":"A reauthorization cannot be made."}]}"""));

        var ex = await Assert.ThrowsAsync<PaymentRequestException>(() => FulfilAsync(orderId));

        Assert.Equal("reauthorization_refused", ex.Code);
        Assert.Contains("REAUTHORIZATION_NOT_ALLOWED", ex.Message);
        Assert.Contains("dbg-reauth", ex.Message);
        Assert.Contains("cancel this order", ex.Message);
        Assert.Equal(0, _host.PayPal.Count("POST", "/capture$"));

        // The claim was released: the operator can still cancel and release the funds.
        var (order, voided) = await _host.InScopeAsync(sp => sp.GetRequiredService<PaymentService>().CancelAsync(orderId, CancellationToken.None));
        Assert.Equal(PaymentStatus.Voided, voided!.Status);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task Fulfil_ReauthorizeUnknown_RecordedAndResent()
    {
        var orderId = await _host.PlaceOrderAsync();
        await PayAsync(orderId);
        _host.Clock.Advance(TimeSpan.FromDays(4));
        var drops = 0;
        _host.PayPal.Intercept = (req, _, _) =>
            req.RequestUri!.AbsolutePath.EndsWith("/reauthorize") && Interlocked.Increment(ref drops) <= 2
                ? throw new HttpRequestException("connection reset")
                : Task.FromResult<HttpResponseMessage?>(null);

        var pending = await Assert.ThrowsAsync<PaymentOutcomePendingException>(() => FulfilAsync(orderId));
        Assert.Equal(PaymentGatewayFailure.Unreachable, pending.Cause.Failure);
        var stuck = (await LoadAsync(orderId)).Payment!;
        Assert.Equal(PaymentStatus.Reauthorizing, stuck.Status);
        Assert.NotNull(stuck.OutcomeUnknownSince);

        var payment = await FulfilAsync(orderId);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.Single(_host.PayPal.Requests.Where(r => r.Path.EndsWith("/reauthorize")).Select(r => r.PayPalRequestId).Distinct());
    }

    [Fact]
    public async Task Fulfil_CaptureConnectionFails_ResendSettles()
    {
        var orderId = await _host.PlaceOrderAsync();
        await PayAsync(orderId);
        _host.PayPal.Intercept = ActThenDropOnce("POST", "/capture$");

        var payment = await FulfilAsync(orderId);

        Assert.Equal(PaymentStatus.Captured, payment.Status);
        var captures = _host.PayPal.Requests.Where(r => r.Path.EndsWith("/capture")).ToList();
        Assert.Equal(2, captures.Count); // the dropped call and its re-send
        Assert.Single(captures.Select(c => c.PayPalRequestId).Distinct()); // same key → PayPal replays, no second capture
    }

    [Fact]
    public async Task Fulfil_ProviderSilent_PendingThenSweeperSettles()
    {
        var orderId = await _host.PlaceOrderAsync();
        await PayAsync(orderId);
        _host.PayPal.Intercept = async (req, body, ct) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/capture")) await Task.Delay(Timeout.Infinite, ct);
            return null;
        };

        var pending = await Assert.ThrowsAsync<PaymentOutcomePendingException>(() => FulfilAsync(orderId));
        Assert.Equal(PaymentGatewayFailure.Timeout, pending.Cause.Failure);

        _host.PayPal.Intercept = null;
        var paymentId = (await LoadAsync(orderId)).Payment!.Id;
        await _host.InScopeAsync(sp => sp.GetRequiredService<PaymentService>().SettleAsync(paymentId, CancellationToken.None));

        var (order, payment) = await LoadAsync(orderId);
        Assert.Equal(PaymentStatus.Captured, payment!.Status);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    [Fact]
    public async Task Fulfil_WithoutAuthorization_IsConflict()
    {
        var orderId = await _host.PlaceOrderAsync();

        var ex = await Assert.ThrowsAsync<PaymentRequestException>(() => FulfilAsync(orderId));

        Assert.Equal(PaymentErrorKind.Conflict, ex.Kind);
    }

    // ───────────── cancel / void ─────────────

    [Fact]
    public async Task Cancel_VoidsTheAuthorization()
    {
        var orderId = await _host.PlaceOrderAsync();
        var paid = await PayAsync(orderId);

        var (order, payment) = await _host.InScopeAsync(sp => sp.GetRequiredService<PaymentService>().CancelAsync(orderId, CancellationToken.None));

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Voided, payment!.Status);
        Assert.Equal("VOIDED", _host.PayPal.AuthorizationStatus(paid.AuthorizationId!));
        await Assert.ThrowsAsync<PaymentRequestException>(() => FulfilAsync(orderId));
    }

    [Fact]
    public async Task Cancel_VoidConnectionFails_SettledFromGetAuthorization()
    {
        var orderId = await _host.PlaceOrderAsync();
        await PayAsync(orderId);
        _host.PayPal.Intercept = ActThenDropOnce("POST", "/void$");

        var (_, payment) = await _host.InScopeAsync(sp => sp.GetRequiredService<PaymentService>().CancelAsync(orderId, CancellationToken.None));

        Assert.Equal(PaymentStatus.Voided, payment!.Status);
        Assert.Equal(1, _host.PayPal.Count("GET", "^/v2/payments/authorizations/[A-Z0-9]+$"));
    }

    [Fact]
    public async Task Cancel_AfterFulfilment_IsRefused()
    {
        var orderId = await _host.PlaceOrderAsync();
        await PayAsync(orderId);
        await FulfilAsync(orderId);

        var ex = await Assert.ThrowsAsync<PaymentRequestException>(() =>
            _host.InScopeAsync(sp => sp.GetRequiredService<PaymentService>().CancelAsync(orderId, CancellationToken.None)));

        Assert.Equal("order_fulfilled", ex.Code);
    }

    // ───────────── refunds ─────────────

    private async Task<int> CapturedOrderAsync()
    {
        var orderId = await _host.PlaceOrderAsync();
        await PayAsync(orderId);
        await FulfilAsync(orderId);
        return orderId;
    }

    [Fact]
    public async Task Refund_PartialTwice_ThenNeverBeyondCaptured()
    {
        var orderId = await CapturedOrderAsync();

        var (_, r1) = await RefundAsync(orderId, 10.00m, "key-1");
        var (afterSecond, r2) = await RefundAsync(orderId, 15.00m, "key-2");

        Assert.Equal(RefundStatus.Succeeded, r1.Status);
        Assert.Equal(RefundStatus.Succeeded, r2.Status);
        Assert.NotEqual(r1.PayPalRefundId, r2.PayPalRefundId);
        Assert.Equal(PaymentStatus.PartiallyRefunded, afterSecond.Status);
        Assert.Equal(26.00m, afterSecond.RefundableAmount);

        var ex = await Assert.ThrowsAsync<PaymentRequestException>(() => RefundAsync(orderId, 26.01m, "key-3"));
        Assert.Equal("exceeds_refundable", ex.Code);
        Assert.Equal(2, _host.PayPal.Count("POST", "/refund$"));

        var (full, r4) = await RefundAsync(orderId, null, "key-4");
        Assert.Equal(26.00m, r4.Amount);
        Assert.Equal(PaymentStatus.Refunded, full.Status);
        await Assert.ThrowsAsync<PaymentRequestException>(() => RefundAsync(orderId, 0.01m, "key-5"));
    }

    [Fact]
    public async Task Refund_SameKey_DoesNotRefundTwice()
    {
        var orderId = await CapturedOrderAsync();

        var (_, first) = await RefundAsync(orderId, 5.00m, "same-key");
        var (payment, second) = await RefundAsync(orderId, 5.00m, "same-key");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, _host.PayPal.Count("POST", "/refund$"));
        Assert.Equal(5.00m, payment.RefundedAmount);
    }

    [Fact]
    public async Task Refund_SameKeyDifferentAmount_IsRejected()
    {
        var orderId = await CapturedOrderAsync();
        await RefundAsync(orderId, 5.00m, "same-key");

        var ex = await Assert.ThrowsAsync<PaymentRequestException>(() => RefundAsync(orderId, 6.00m, "same-key"));

        Assert.Equal("idempotency_key_reused", ex.Code);
    }

    [Fact]
    public async Task Refund_ConcurrentDistinctKeys_NeverOverRefund()
    {
        var orderId = await CapturedOrderAsync();

        var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            RefundAsync(orderId, 30.00m, $"k{i}").ContinueWith(t => t.IsCompletedSuccessfully)));

        var (_, payment) = await LoadAsync(orderId);
        Assert.True(payment!.RefundedAmount <= payment.CapturedAmount);
        Assert.Equal(1, attempts.Count(ok => ok));
    }

    [Fact]
    public async Task Refund_ConnectionFails_ResendWithSameKeySettles()
    {
        var orderId = await CapturedOrderAsync();
        _host.PayPal.Intercept = ActThenDropOnce("POST", "/refund$");

        var (payment, refund) = await RefundAsync(orderId, 7.00m, "flaky");

        Assert.Equal(RefundStatus.Succeeded, refund.Status);
        Assert.Equal(7.00m, payment.RefundedAmount);
        Assert.Single(_host.PayPal.Requests.Where(r => r.Path.EndsWith("/refund")).Select(r => r.PayPalRequestId).Distinct());
    }

    [Fact]
    public async Task Refund_ProviderSilent_RepeatWithSameKeySettlesOnce()
    {
        var orderId = await CapturedOrderAsync();
        var hangs = 0;
        _host.PayPal.Intercept = async (req, body, ct) =>
        {
            // Both the call and its in-request re-send go unanswered, exhausting the request's PayPal budget.
            if (req.RequestUri!.AbsolutePath.EndsWith("/refund") && Interlocked.Increment(ref hangs) <= 2)
            {
                if (hangs == 1) await ForwardAsync(_host.PayPal, req, body, ct); // PayPal refunds…
                await Task.Delay(Timeout.Infinite, ct); // …and never answers
            }
            return null;
        };

        await Assert.ThrowsAsync<PaymentOutcomePendingException>(() => RefundAsync(orderId, 9.00m, "slow"));
        var (payment, refund) = await RefundAsync(orderId, 9.00m, "slow");

        Assert.Equal(RefundStatus.Succeeded, refund.Status);
        Assert.Equal(9.00m, payment.RefundedAmount);
        Assert.Equal(42.00m, payment.RefundableAmount);
    }

    [Fact]
    public async Task Refund_OtherShoppersOrder_IsNotFound()
    {
        var orderId = await CapturedOrderAsync();

        var ex = await Assert.ThrowsAsync<PaymentRequestException>(() => RefundAsync(orderId, 1.00m, "k", PaymentTestHost.OtherBuyer));

        Assert.Equal(PaymentErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task Refund_BeforeFulfilment_IsConflict()
    {
        var orderId = await _host.PlaceOrderAsync();
        await PayAsync(orderId);

        var ex = await Assert.ThrowsAsync<PaymentRequestException>(() => RefundAsync(orderId, 1.00m, "k"));

        Assert.Equal("not_captured", ex.Code);
    }

    [Fact]
    public async Task ClaimStore_RefusesSecondClaimFromAnotherContext()
    {
        var first = await _host.InScopeAsync(sp => sp.GetRequiredService<IPaymentClaimStore>().TryClaimAsync("k", CancellationToken.None));
        var second = await _host.InScopeAsync(sp => sp.GetRequiredService<IPaymentClaimStore>().TryClaimAsync("k", CancellationToken.None));
        var sameContext = await _host.InScopeAsync(async sp =>
        {
            var store = sp.GetRequiredService<IPaymentClaimStore>();
            return (await store.TryClaimAsync("j", CancellationToken.None), await store.TryClaimAsync("j", CancellationToken.None));
        });

        Assert.True(first);
        Assert.False(second);
        Assert.Equal((true, false), sameContext);
    }

    [Fact]
    public async Task CardNumber_IsNeverLogged()
    {
        var orderId = await _host.PlaceOrderAsync();
        await PayAsync(orderId);
        await _host.InScopeAsync(sp => sp.GetRequiredService<PaymentMethodService>().SaveAsync(PaymentTestHost.Buyer, TestCard(), CancellationToken.None));
        var declinedOrder = await _host.PlaceOrderAsync();
        await Assert.ThrowsAsync<PaymentGatewayException>(() => PayAsync(declinedOrder, card: TestCard(FakePayPal.DeclinedCardNumber)));

        Assert.NotEmpty(_host.Logs);
        Assert.DoesNotContain(_host.Logs, l => l.Contains("4111111111111111") || l.Contains(FakePayPal.DeclinedCardNumber) || l.Contains("security_code"));
    }

    /// <summary>Delivers a request straight to the fake's routing (bypassing its interceptor), so "PayPal acts".</summary>
    internal static async Task ForwardAsync(FakePayPal fake, HttpRequestMessage req, string? body, CancellationToken ct)
    {
        var intercept = fake.Intercept;
        fake.Intercept = null;
        try
        {
            using var invoker = new HttpMessageInvoker(fake, disposeHandler: false);
            var copy = Clone(req, body);
            copy.Headers.Add(FakePayPal.ForwardedHeader, "1");
            (await invoker.SendAsync(copy, ct)).Dispose();
        }
        finally
        {
            fake.Intercept = intercept;
        }
    }

    private static HttpRequestMessage Clone(HttpRequestMessage req, string? body)
    {
        var copy = new HttpRequestMessage(req.Method, req.RequestUri);
        foreach (var h in req.Headers) copy.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (body is not null) copy.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        return copy;
    }

}
