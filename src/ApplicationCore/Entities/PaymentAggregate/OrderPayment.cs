using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money-movement state that follows an <see cref="OrderAggregate.Order"/>. This is additive: the order
/// aggregate is unchanged and carries no payment state. One <see cref="OrderPayment"/> exists per order
/// (enforced by a unique index on <see cref="OrderId"/>), and it owns the PayPal-side identifiers and current
/// status for the hold, the capture and the refunds so a later request can act on the payment.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, decimal authorizedAmount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrWhiteSpace(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        Status = PaymentStatus.PendingPayment;
        // A stable, globally-unique seed for deriving PayPal idempotency keys. Persisted with the row, so it
        // is identical across retries and across hosts for the same logical payment (unlike the DB id, which
        // an in-memory provider reuses across restarts).
        IdempotencySeed = Guid.NewGuid().ToString("N");
    }

    /// <summary>Stable seed for deriving PayPal idempotency keys (PayPal-Request-Id) for this payment.</summary>
    public string IdempotencySeed { get; private set; }

    /// <summary>The existing order this payment settles.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owner of the order/payment (the authenticated shopper). Scopes all shopper access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total captured at authorization time, in <see cref="Currency"/>, to the cent.</summary>
    public decimal AuthorizedAmount { get; private set; }

    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- PayPal-owned identifiers (the state a later request needs) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? CaptureId { get; private set; }

    // --- What PayPal reported at capture ---
    public decimal? CapturedGross { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- PayPal event timestamps, used for reconciliation (never local row-creation time) ---
    public DateTimeOffset? AuthorizedAtUtc { get; private set; }
    public DateTimeOffset? CapturedAtUtc { get; private set; }

    /// <summary>If a saved card funded the authorization, the vault id used (never card details).</summary>
    public string? FundingVaultId { get; private set; }

    /// <summary>Operator-actionable reason a transition could not complete (e.g. auth can no longer be renewed).</summary>
    public string? FailureReason { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    // ---------------------------------------------------------------------------------------------
    // Transitions. Each is a no-op-safe state change so a double-click cannot move money twice; the
    // orchestration layer gates outbound PayPal calls on the current status.
    // ---------------------------------------------------------------------------------------------

    public void SetFundingVaultId(string vaultId) => FundingVaultId = vaultId;

    /// <summary>Record the PayPal order id as soon as it exists (before the hold is confirmed).</summary>
    public void AttachPayPalOrder(string payPalOrderId)
    {
        Guard.Against.NullOrWhiteSpace(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
    }

    /// <summary>The hold is in place.</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, DateTimeOffset? authorizedAtUtc)
    {
        Guard.Against.NullOrWhiteSpace(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrWhiteSpace(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizedAtUtc = authorizedAtUtc;
        FailureReason = null;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>A stale authorization was renewed; the authorization id PayPal now honours has changed.</summary>
    public void RenewAuthorization(string newAuthorizationId, DateTimeOffset? authorizedAtUtc)
    {
        Guard.Against.NullOrWhiteSpace(newAuthorizationId, nameof(newAuthorizationId));
        AuthorizationId = newAuthorizationId;
        if (authorizedAtUtc.HasValue)
        {
            AuthorizedAtUtc = authorizedAtUtc;
        }
    }

    /// <summary>The hold was captured at fulfilment; money has moved.</summary>
    public void MarkFulfilled(string captureId, decimal capturedGross, decimal? payPalFee, decimal? netAmount, DateTimeOffset? capturedAtUtc)
    {
        Guard.Against.NullOrWhiteSpace(captureId, nameof(captureId));
        CaptureId = captureId;
        CapturedGross = capturedGross;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAtUtc = capturedAtUtc;
        FailureReason = null;
        Status = PaymentStatus.Fulfilled;
    }

    /// <summary>The hold was released before fulfilment.</summary>
    public void MarkCancelled()
    {
        Status = PaymentStatus.Cancelled;
    }

    public void MarkFailed(string reason)
    {
        FailureReason = reason;
        Status = PaymentStatus.Failed;
    }

    // --- Refund ledger ---

    /// <summary>Sum of refunds that count against the captured amount (pending + completed).</summary>
    public decimal TotalRefunded() => _refunds.Where(r => r.CountsTowardRefundedTotal).Sum(r => r.Amount);

    /// <summary>The amount that is still refundable given what was captured and already refunded.</summary>
    public decimal RefundableRemaining() => (CapturedGross ?? AuthorizedAmount) - TotalRefunded();

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Add a refund claim. Rejects a refund that would exceed the captured amount so a partly-refunded
    /// order never becomes refundable beyond what was captured.
    /// </summary>
    public PaymentRefund AddRefund(string idempotencyKey, decimal amount)
    {
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (amount > RefundableRemaining())
        {
            throw new InvalidOperationException(
                $"Refund of {amount} {Currency} exceeds the refundable remaining {RefundableRemaining()} {Currency} for order {OrderId}.");
        }

        var refund = new PaymentRefund(idempotencyKey, amount, Currency);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>Recompute the aggregate status after a refund settles.</summary>
    public void RecomputeRefundStatus()
    {
        var captured = CapturedGross ?? AuthorizedAmount;
        var refunded = _refunds.Where(r => r.State == RefundState.Completed).Sum(r => r.Amount);
        if (refunded <= 0m)
        {
            // No completed refunds; leave as Fulfilled unless nothing was ever captured.
            if (Status is PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                Status = PaymentStatus.Fulfilled;
            }
            return;
        }
        Status = refunded >= captured ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }
}
