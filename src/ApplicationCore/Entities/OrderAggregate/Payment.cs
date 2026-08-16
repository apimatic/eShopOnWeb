using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Carries the payment state that PayPal owns for an order: the ids and current status of the
/// hold (authorization), the capture and each refund, plus the money figures PayPal reported at
/// capture (captured amount, fee, net proceeds). It is part of the <see cref="Order"/> aggregate
/// so a later request can act on it, not only the one that started it.
/// </summary>
public class Payment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(string currency, decimal amount)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        Currency = currency;
        Amount = amount;
        Status = PaymentStatus.Pending;
    }

    /// <summary>Currency the order is charged in (from configuration).</summary>
    public string Currency { get; private set; }

    /// <summary>The order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal order (checkout) id created to carry the payment.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>PayPal authorization id (the hold).</summary>
    public string? AuthorizationId { get; private set; }

    public string? AuthorizationStatus { get; private set; }

    /// <summary>When the current authorization stops being honoured; used to detect staleness before capture.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>PayPal capture id (the money taken at fulfilment).</summary>
    public string? CaptureId { get; private set; }

    public string? CaptureStatus { get; private set; }

    /// <summary>Gross amount PayPal captured.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>PayPal's fee on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    public PaymentStatus Status { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total amount already refunded against the capture.</summary>
    public decimal RefundedAmount => _refunds.Sum(r => r.Amount);

    /// <summary>Records the authorization (hold) returned by PayPal.</summary>
    public void SetAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replaces the authorization after a stale hold is renewed (reauthorized) before capture.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records the capture (money taken) and the figures PayPal reported.</summary>
    public void SetCapture(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    /// <summary>Records that the hold was released (voided) before any capture.</summary>
    public void SetVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>Returns an existing refund created under the same idempotency key, if any.</summary>
    public Refund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// The amount that can still be refunded: the captured total minus what has already been
    /// refunded. Guarantees a partly-refunded order never becomes refundable beyond what was captured.
    /// </summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - RefundedAmount;

    /// <summary>
    /// Adds a refund, enforcing that the running refunded total never exceeds the captured amount.
    /// </summary>
    public Refund AddRefund(string idempotencyKey, decimal amount, string? payPalRefundId, string status)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException("Only a captured payment can be refunded.");
        }

        if (amount > RefundableRemaining)
        {
            throw new InvalidOperationException(
                $"Refund of {amount} exceeds the refundable remaining {RefundableRemaining} on this capture.");
        }

        var refund = new Refund(idempotencyKey, amount, payPalRefundId, status);
        _refunds.Add(refund);

        Status = RefundedAmount >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }
}
