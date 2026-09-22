using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Payment/fulfilment state for an <see cref="Order"/>, kept as a satellite aggregate keyed by
/// <see cref="OrderId"/> so the existing order/order-item model is reused rather than duplicated.
/// Holds enough of the state PayPal owns (ids and current status for the hold, the capture and the
/// refunds) that a later request can act on it. Created in state <see cref="PaymentState.AwaitingPayment"/>
/// at order placement, before any PayPal call.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.Negative(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        State = PaymentState.AwaitingPayment;
        // Correlation handle reconciliation joins on, and a per-order seed for deterministic,
        // run-unique PayPal-Request-Id idempotency keys.
        IdempotencyKey = Guid.NewGuid().ToString("N");
        // Run-unique invoice id: the in-memory store resets order ids each run, and PayPal enforces
        // invoice-id uniqueness across the account, so the id embeds the per-order GUID as well as the
        // (human-readable) order id. Reconciliation matches on the full string; the order id is parsed
        // back only for display of PayPal-only transactions.
        InvoiceId = $"eshop-{orderId}-{IdempotencyKey.Substring(0, 12)}";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string InvoiceId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public PaymentState State { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>When the hold was placed (money-movement clock, not row creation).</summary>
    public DateTimeOffset? AuthorizedAt { get; private set; }
    /// <summary>When the capture took the money — the reconciliation clock for this payment.</summary>
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    // Deterministic, per-order idempotency keys for each PayPal write. Run-unique via the GUID seed.
    public string CreateRequestId => $"{IdempotencyKey}-create";
    public string AuthorizeRequestId => $"{IdempotencyKey}-authorize";
    public string CaptureRequestId => $"{IdempotencyKey}-capture";
    public string VoidRequestId => $"{IdempotencyKey}-void";
    public string ReauthorizeRequestId => $"{IdempotencyKey}-reauth";

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        State = PaymentState.Authorized;
        AuthorizedAt = DateTimeOffset.UtcNow;
        FailureReason = null;
    }

    /// <summary>Records a renewed authorization id after a stale hold was reauthorized before capture.</summary>
    public void ReplaceAuthorization(string authorizationId, string? authorizationStatus)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? paypalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        State = PaymentState.Fulfilled;
        CapturedAt = DateTimeOffset.UtcNow;
        FailureReason = null;
    }

    public void MarkCancelled(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        State = PaymentState.Cancelled;
    }

    public void MarkFailed(string reason)
    {
        FailureReason = reason;
        State = PaymentState.Failed;
    }

    /// <summary>Amount already refunded (or pending refund) against the capture.</summary>
    public decimal RefundedAmount() => _refunds.Where(r => r.CountsAgainstCapture).Sum(r => r.Amount);

    /// <summary>What can still be refunded without exceeding the captured amount.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - RefundedAmount();

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount)
    {
        var refund = new PaymentRefund(idempotencyKey, amount);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>Rolls the aggregate state up after a refund result is recorded.</summary>
    public void RecomputeRefundState()
    {
        var refunded = RefundedAmount();
        if (refunded <= 0m) return;
        State = refunded >= (CapturedAmount ?? 0m) ? PaymentState.Refunded : PaymentState.PartiallyRefunded;
    }
}
