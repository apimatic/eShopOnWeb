using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The money side of an <see cref="Order"/>. Carries enough of the state PayPal owns — the ids and
/// current status of the hold (authorization), the capture, and any refunds — that a later request
/// can act on it, not only the one that created it.
/// </summary>
public class Payment : BaseEntity
{
    private readonly List<Refund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    /// <summary>
    /// Created when an order is authorized: PayPal is holding <paramref name="amount"/> against the
    /// checkout order <paramref name="payPalOrderId"/> under authorization <paramref name="authorizationId"/>.
    /// </summary>
    public Payment(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt, decimal amount, string currency, string invoiceId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        Amount = amount;
        Currency = currency;
        InvoiceId = invoiceId;
        Status = PaymentStatus.Authorized;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal checkout order id (checkout_orders_v2 resource).</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>
    /// Unique invoice id tagged onto the PayPal order/capture, so PayPal's transaction reporting can
    /// be lined back up to this eShop order during reconciliation.
    /// </summary>
    public string InvoiceId { get; private set; }

    /// <summary>Currency the money moves in (from <c>PayPal:Currency</c> configuration).</summary>
    public string Currency { get; private set; }

    /// <summary>The authorized amount, equal to the order total to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- Authorization (the hold) ---
    public string AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture (money taken) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }

    /// <summary>Gross amount PayPal reported capturing.</summary>
    public decimal? CapturedGross { get; private set; }

    /// <summary>PayPal's fee on the capture, as PayPal reported it.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant, as PayPal reported them.</summary>
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    /// <summary>
    /// Records that the authorization was renewed (reauthorized) because it had gone stale before
    /// fulfilment. The old hold is replaced by a fresh one PayPal issued.
    /// </summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Authorized)
            throw new InvalidOperationException($"Cannot renew authorization for a payment in status {Status}.");

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records the capture PayPal performed at fulfilment, with the amounts PayPal reported.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedGross,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
            throw new InvalidOperationException($"Cannot capture a payment in status {Status}.");

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGross = capturedGross;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    /// <summary>Records that the hold was released (voided) on a cancel-before-fulfilment.</summary>
    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized)
            throw new InvalidOperationException($"Cannot void a payment in status {Status}.");

        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>Total already refunded against the capture across all recorded refunds.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>Amount still refundable: the captured gross minus what has already been refunded.</summary>
    public decimal RefundableRemaining() => (CapturedGross ?? 0m) - TotalRefunded();

    /// <summary>Returns the existing refund created under <paramref name="idempotencyKey"/>, if any.</summary>
    public Refund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Records a refund against the capture. Guards that a capture exists and that the refund does
    /// not take the total refunded beyond the captured amount, so a partly-refunded order never
    /// becomes refundable beyond what was captured.
    /// </summary>
    public void AddRefund(Refund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
            throw new InvalidOperationException($"Cannot refund a payment in status {Status}.");
        if (refund.Amount > RefundableRemaining())
            throw new InvalidOperationException(
                $"Refund of {refund.Amount} exceeds refundable remaining {RefundableRemaining()}.");

        _refunds.Add(refund);
        Status = RefundableRemaining() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }
}
