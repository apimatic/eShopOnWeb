using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Takes and gives back money for orders. Every write to the payment provider follows the same order:
/// claim (saved before the call, so a second caller is turned away) → provider call → record the result.
/// A call whose outcome is unknown is re-sent with the same idempotency key, so it can never charge or refund twice.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    /// <summary>
    /// A claim older than this whose sender never recorded an outcome (crash, lost process) is treated as unknown
    /// and may be re-sent with its original idempotency key. Well above the longest provider call budget.
    /// </summary>
    public static readonly TimeSpan StaleClaimAfter = TimeSpan.FromMinutes(2);

    private const string PaymentOperation = "payment";
    private const string RefundOperation = "refund";

    private readonly IOrderPaymentStore _store;
    private readonly IPaymentGateway _gateway;
    private readonly TimeProvider _clock;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(IOrderPaymentStore store, IPaymentGateway gateway, TimeProvider clock,
        IAppLogger<OrderPaymentService> logger)
    {
        _store = store;
        _gateway = gateway;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PayOrderResult> PayAsync(int orderId, string buyerId, EncryptedCard card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        ClaimSnapshot? snapshot;
        try
        {
            snapshot = await _store.UpdateAsync(orderId, order =>
            {
                if (order.BuyerId != buyerId)
                    return null; // another shopper's order looks exactly like a missing one

                var amountMinor = Money.ToMinorUnits(order.Total(), _gateway.Currency);
                if (amountMinor <= 0)
                    throw new OrderPaymentException(OrderPaymentError.InvalidAmount, "The order total must be greater than zero.");

                var paymentClaim = order.ClaimPayment(amountMinor, _gateway.Currency, _clock.GetUtcNow(), StaleClaimAfter);
                return new ClaimSnapshot(paymentClaim, order.PaymentStatus);
            }, cancellationToken);
        }
        catch (OrderPaymentException ex)
        {
            return new PayOrderResult(PayOrderOutcome.Invalid, orderId, null, null, ex.Message);
        }

        if (snapshot is null)
            return new PayOrderResult(PayOrderOutcome.NotFound, orderId, null, null, $"Order {orderId} was not found.");

        var claim = snapshot.Claim;

        switch (claim.Kind)
        {
            case PaymentClaimKind.AlreadyPaid:
                return new PayOrderResult(PayOrderOutcome.AlreadyPaid, orderId, snapshot.PaymentStatus, claim.Attempt,
                    "This order has already been paid. You have not been charged again.");
            case PaymentClaimKind.InProgress:
                return new PayOrderResult(PayOrderOutcome.InProgress, orderId, OrderPaymentStatus.PaymentPending, claim.Attempt,
                    "A payment for this order is already being processed. Please wait a moment before checking your order.");
            case PaymentClaimKind.AwaitingProviderOutcome:
                return new PayOrderResult(PayOrderOutcome.Pending, orderId, OrderPaymentStatus.PaymentPending, claim.Attempt,
                    ShopperMessages.Pending);
        }

        var attempt = claim.Attempt;
        var request = new CardPaymentRequest(attempt.IdempotencyKey, attempt.MerchantReference, attempt.AmountMinor,
            attempt.Currency, card);

        // The shopper's request may be aborted mid-call; the charge must still be recorded, so the provider call
        // and the bookkeeping after it are deliberately not tied to the caller's cancellation token.
        var exchanges = new List<CardPaymentResult>();
        var result = await _gateway.ChargeCardAsync(request, CancellationToken.None);
        exchanges.Add(result);
        if (result.Outcome == CardPaymentOutcome.Unknown)
        {
            // Settle the unknown outcome now: re-send with the SAME idempotency key so the provider returns the
            // original result instead of charging again.
            _logger.LogWarning("Payment outcome unknown for order {OrderId} attempt {AttemptNumber} ({Reference}); re-sending with the same idempotency key.",
                orderId, attempt.AttemptNumber, attempt.MerchantReference);
            result = await _gateway.ChargeCardAsync(request, CancellationToken.None);
            exchanges.Add(result);
        }

        var (status, outcome, message) = Classify(result);
        var recorded = await _store.UpdateAsync(orderId, order =>
        {
            var now = _clock.GetUtcNow();
            foreach (var exchange in exchanges)
            {
                order.AddPaymentRecordEntry(_gateway.ProviderName, PaymentOperation, attempt.AttemptNumber, null,
                    attempt.IdempotencyKey, attempt.MerchantReference, exchange.Exchange.HttpStatus,
                    exchange.Exchange.ResponseBody, exchange.Exchange.TransportError, now);
            }
            order.RecordPaymentOutcome(attempt.AttemptNumber, status, result.PspReference, result.ResultCode,
                result.RefusalReason, result.RefusalReasonCode, message, now);
            return order;
        }, CancellationToken.None);

        _logger.LogInformation("Payment for order {OrderId} attempt {AttemptNumber} ({Reference}): {Outcome}, pspReference {PspReference}, resultCode {ResultCode}, refusalReasonCode {RefusalReasonCode}, errorCode {ErrorCode}.",
            orderId, attempt.AttemptNumber, attempt.MerchantReference, result.Outcome, result.PspReference ?? "-",
            result.ResultCode ?? "-", result.RefusalReasonCode ?? "-", result.ErrorCode ?? "-");

        var recordedAttempt = recorded?.PaymentAttempts.Single(a => a.AttemptNumber == attempt.AttemptNumber) ?? attempt;
        return new PayOrderResult(outcome, orderId, recorded?.PaymentStatus, recordedAttempt, message);
    }

    public async Task<RefundOrderResult> RefundAsync(int orderId, decimal? amount, string? reason, string? idempotencyKey,
        string requestedBy, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(requestedBy, nameof(requestedBy));

        // Settle refunds of this order whose outcome is still unknown before deciding what is left to refund.
        await SettleUnsettledRefundsAsync(orderId, cancellationToken);

        var refundId = string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid() : RefundIdFor(orderId, idempotencyKey);

        RefundPlan? plan;
        try
        {
            plan = await _store.UpdateAsync(orderId, order =>
            {
                long? amountMinor = amount is null
                    ? null
                    : Money.ToMinorUnits(amount.Value, order.AuthorisedPayment?.Currency ?? _gateway.Currency);
                var claim = order.ClaimRefund(refundId, refundId.ToString(), amountMinor, reason, requestedBy,
                    _clock.GetUtcNow(), StaleClaimAfter);
                var payment = order.PaymentAttempts.Single(a => a.AttemptNumber == claim.Refund.PaymentAttemptNumber);
                return new RefundPlan(claim, payment.PspReference!);
            }, cancellationToken);
        }
        catch (OrderPaymentException ex)
        {
            return new RefundOrderResult(ex.Error == OrderPaymentError.NotPaid ? RefundOrderOutcome.NotPaid : RefundOrderOutcome.Invalid,
                orderId, null, null, ex.Message);
        }

        if (plan is null)
            return new RefundOrderResult(RefundOrderOutcome.NotFound, orderId, null, null, $"Order {orderId} was not found.");

        switch (plan.Claim.Kind)
        {
            case RefundClaimKind.Existing:
                return new RefundOrderResult(RefundOrderOutcome.Existing, orderId, null, plan.Claim.Refund,
                    "A refund with this idempotency key already exists; no new refund was made.");
            case RefundClaimKind.InProgress:
                return new RefundOrderResult(RefundOrderOutcome.InProgress, orderId, null, plan.Claim.Refund,
                    "A refund with this idempotency key is already being processed.");
        }

        return await SendRefundAsync(orderId, plan.Claim.Refund, plan.PaymentPspReference);
    }

    private async Task SettleUnsettledRefundsAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _store.GetAsync(orderId, includePaymentRecord: false, cancellationToken);
        if (order is null)
            return;

        var now = _clock.GetUtcNow();
        var unsettled = order.Refunds
            .Where(r => r.Status == RefundStatus.Unknown || (r.Status == RefundStatus.Requested && now - r.LastSentAt >= StaleClaimAfter))
            .Select(r => r.Id)
            .ToList();

        foreach (var refundId in unsettled)
        {
            var plan = await _store.UpdateAsync(orderId, o =>
            {
                if (!o.TryReopenRefund(refundId, _clock.GetUtcNow(), StaleClaimAfter))
                    return null;
                var refund = o.Refunds.Single(r => r.Id == refundId);
                var payment = o.PaymentAttempts.Single(a => a.AttemptNumber == refund.PaymentAttemptNumber);
                return new RefundPlan(new RefundClaim(RefundClaimKind.Resend, refund), payment.PspReference!);
            }, cancellationToken);

            if (plan is not null)
            {
                _logger.LogWarning("Settling refund {RefundId} of order {OrderId} whose outcome was unknown; re-sending with the same idempotency key.",
                    refundId, orderId);
                await SendRefundAsync(orderId, plan.Claim.Refund, plan.PaymentPspReference);
            }
        }
    }

    private async Task<RefundOrderResult> SendRefundAsync(int orderId, OrderRefund refund, string paymentPspReference)
    {
        var request = new RefundRequest(refund.IdempotencyKey, refund.MerchantReference, paymentPspReference,
            refund.AmountMinor, refund.Currency);

        var exchanges = new List<RefundResult>();
        var result = await _gateway.RefundAsync(request, CancellationToken.None);
        exchanges.Add(result);
        if (result.Outcome == RefundOutcome.Unknown)
        {
            _logger.LogWarning("Refund outcome unknown for order {OrderId} refund {RefundId} ({Reference}); re-sending with the same idempotency key.",
                orderId, refund.Id, refund.MerchantReference);
            result = await _gateway.RefundAsync(request, CancellationToken.None);
            exchanges.Add(result);
        }

        var (status, outcome, message) = result.Outcome switch
        {
            RefundOutcome.Received => (RefundStatus.Received, RefundOrderOutcome.Refunded, (string?)null),
            RefundOutcome.Rejected when result.IsConfigurationError => (RefundStatus.Failed, RefundOrderOutcome.ProviderUnavailable,
                "The payment provider refused our credentials; no money was given back."),
            RefundOutcome.Rejected => (RefundStatus.Failed, RefundOrderOutcome.Rejected,
                $"The payment provider rejected the refund: {result.ErrorMessage ?? "no reason given"}."),
            _ => (RefundStatus.Unknown, RefundOrderOutcome.Unknown,
                "The payment provider could not be reached to confirm the refund. It still counts against the refundable balance; it is re-checked on the next refund request for this order."),
        };

        var recorded = await _store.UpdateAsync(orderId, order =>
        {
            var now = _clock.GetUtcNow();
            foreach (var exchange in exchanges)
            {
                order.AddPaymentRecordEntry(_gateway.ProviderName, RefundOperation, refund.PaymentAttemptNumber, refund.Id,
                    refund.IdempotencyKey, refund.MerchantReference, exchange.Exchange.HttpStatus,
                    exchange.Exchange.ResponseBody, exchange.Exchange.TransportError, now);
            }
            order.RecordRefundOutcome(refund.Id, status, result.PspReference, status == RefundStatus.Failed ? message : null, now);
            return order;
        }, CancellationToken.None);

        _logger.LogInformation("Refund {RefundId} of order {OrderId} ({Reference}, {AmountMinor} {Currency} minor units): {Outcome}, pspReference {PspReference}, errorCode {ErrorCode}.",
            refund.Id, orderId, refund.MerchantReference, refund.AmountMinor, refund.Currency, result.Outcome,
            result.PspReference ?? "-", result.ErrorCode ?? "-");

        var recordedRefund = recorded?.Refunds.Single(r => r.Id == refund.Id) ?? refund;
        return new RefundOrderResult(outcome, orderId, recorded?.PaymentStatus, recordedRefund, message, recorded?.RefundableMinor);
    }

    private static (PaymentAttemptStatus Status, PayOrderOutcome Outcome, string Message) Classify(CardPaymentResult result) =>
        result.Outcome switch
        {
            CardPaymentOutcome.Authorised => (PaymentAttemptStatus.Authorised, PayOrderOutcome.Paid, ShopperMessages.Paid),
            CardPaymentOutcome.Refused => (PaymentAttemptStatus.Refused, PayOrderOutcome.Declined,
                ShopperMessages.ForRefusal(result.RefusalReason)),
            CardPaymentOutcome.Failed => (PaymentAttemptStatus.Failed, PayOrderOutcome.Declined,
                ShopperMessages.ForFailure(result.RefusalReason)),
            CardPaymentOutcome.ActionRequired => (PaymentAttemptStatus.ActionRequired, PayOrderOutcome.Declined,
                ShopperMessages.ActionRequired),
            CardPaymentOutcome.Pending => (PaymentAttemptStatus.Pending, PayOrderOutcome.Pending, ShopperMessages.Pending),
            CardPaymentOutcome.Rejected when result.IsConfigurationError => (PaymentAttemptStatus.Failed,
                PayOrderOutcome.ProviderUnavailable, ShopperMessages.ProviderUnavailable),
            CardPaymentOutcome.Rejected when (result.Exchange.HttpStatus ?? 0) >= 500 => (PaymentAttemptStatus.Failed,
                PayOrderOutcome.ProviderUnavailable, ShopperMessages.ProviderUnavailable),
            CardPaymentOutcome.Rejected => (PaymentAttemptStatus.Failed, PayOrderOutcome.InvalidCard,
                ShopperMessages.ForInvalidCard(result.ErrorMessage)),
            _ => (PaymentAttemptStatus.Unknown, PayOrderOutcome.Unknown, ShopperMessages.Unknown),
        };

    /// <summary>The same operator key on the same order always maps to the same refund (and so to one refund only).</summary>
    public static Guid RefundIdFor(int orderId, string idempotencyKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"order:{orderId}:refund:{idempotencyKey.Trim()}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private sealed record ClaimSnapshot(PaymentClaim Claim, OrderPaymentStatus PaymentStatus);

    private sealed record RefundPlan(RefundClaim Claim, string PaymentPspReference);
}
