using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment for an <see cref="Order"/>. Part of the Order aggregate (one payment per order).
/// It carries enough of the state PayPal owns — the ids and current status for the hold, the
/// capture and the refunds — that a later request (fulfil, cancel, refund) can act on it, not
/// only the request that created it. No card number is ever stored here.
/// </summary>
public class Payment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(
        string payPalOrderId,
        string payPalCustomId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt,
        decimal amount,
        string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(payPalCustomId, nameof(payPalCustomId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalOrderId = payPalOrderId;
        PayPalCustomId = payPalCustomId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        Amount = amount;
        Currency = currency;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>The v2 checkout order id created at PayPal.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>
    /// The globally-unique correlation token we stamped onto the PayPal order as custom_id. Used to
    /// line up PayPal's transaction report against this order without colliding across runs.
    /// </summary>
    public string PayPalCustomId { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }

    // --- The hold (authorization) ---
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset AuthorizedAt { get; private set; }

    // --- The capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // --- The refunds ---
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>
    /// Replace the current authorization with a freshly reauthorized one (new id/status/expiry).
    /// Used when an authorization has gone stale before fulfilment.
    /// </summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException($"Only an authorized payment can be reauthorized (current status: {Status}).");
        }
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>Sum of refunds that count against the captured total.</summary>
    public decimal TotalRefunded() => _refunds.Where(r => r.CountsTowardRefundedTotal).Sum(r => r.Amount);

    /// <summary>How much of the capture can still be refunded.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Record a refund and advance the payment status. Callers must ensure the amount does not
    /// exceed <see cref="RefundableRemaining"/> before calling PayPal; this method enforces the
    /// same invariant defensively so a payment can never be refunded beyond what was captured.
    /// </summary>
    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        if (Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidOperationException($"Only a captured payment can be refunded (current status: {Status}).");
        }
        if (amount > RefundableRemaining())
        {
            throw new InvalidOperationException(
                $"Refund of {amount} exceeds the refundable remaining amount of {RefundableRemaining()}.");
        }

        var refund = new PaymentRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        Status = RefundableRemaining() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}
