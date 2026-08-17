using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment and fulfilment record for a single <see cref="OrderAggregate.Order"/>.
/// This is the additive money-movement state the base order model never carried: it holds the
/// PayPal ids and current status for the hold (authorization), the capture, and the refunds,
/// so a later request can act on the payment, not only the one that started it.
///
/// One <see cref="Payment"/> per order. Modelled as its own aggregate root linked to the order
/// by <see cref="OrderId"/>; the order/order-item model is reused unchanged.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, string currencyCode, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this payment settles.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owning shopper (ASP.NET Identity user name / email), used for shopper scoping.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>Order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Number of authorize attempts made. Drives a deterministic PayPal-Request-Id per attempt so
    /// concurrent double-clicks in the same attempt de-duplicate at PayPal, while a genuine retry
    /// after a declined card gets a fresh idempotency key.
    /// </summary>
    public int AuthorizeAttempts { get; private set; }

    public void IncrementAuthorizeAttempt() => AuthorizeAttempts++;

    // ---- Hold (authorization) state owned by PayPal ----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    // ---- Capture state owned by PayPal ----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>Safe description of the instrument used, e.g. "VISA ****1111". Never full card details.</summary>
    public string? InstrumentDescription { get; private set; }

    /// <summary>Vault id used to pay, if a saved card was used (for traceability); never card details.</summary>
    public string? VaultId { get; private set; }

    // ---- Refunds ----
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Sum of refunds that PayPal has accepted (completed) against this payment.</summary>
    public decimal RefundedAmount => _refunds.Where(r => r.IsCompleted).Sum(r => r.Amount);

    /// <summary>How much of the captured amount is still refundable.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - RefundedAmount;

    /// <summary>
    /// The eShop reference we tag PayPal transactions with, for reconciliation. Deterministic per
    /// payment (so retries and concurrent double-clicks reuse it) yet unique per merchant across
    /// runs — PayPal rejects a duplicate invoice_id, and the in-memory store restarts order ids at 1.
    /// </summary>
    public string InvoiceReference => $"ESHOP-ORDER-{OrderId}-{CreatedAt.UtcTicks}";

    public bool IsAuthorized => AuthorizationId is not null;
    public bool IsCaptured => CaptureId is not null;

    /// <summary>Records a successful authorization (hold placed). Idempotent in effect.</summary>
    public void MarkAuthorized(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiresAt,
        string? instrumentDescription,
        string? vaultId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        if (Status != PaymentStatus.AwaitingPayment && Status != PaymentStatus.Failed)
        {
            throw new InvalidOperationException(
                $"Order {OrderId} cannot be authorized because its payment is '{Status}'.");
        }

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
        InstrumentDescription = instrumentDescription;
        VaultId = vaultId;
        Status = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationFailed()
    {
        Status = PaymentStatus.Failed;
    }

    /// <summary>Replaces the current hold with a renewed one (reauthorization) before capture.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Order {OrderId} authorization cannot be renewed because its payment is '{Status}'.");
        }
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records the capture taken at fulfilment: captured amount, PayPal's fee, and net proceeds.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Order {OrderId} cannot be fulfilled because its payment is '{Status}'.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Fulfilled;
    }

    /// <summary>Records that the hold was released (voided) on cancellation before fulfilment.</summary>
    public void MarkCancelled()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Order {OrderId} cannot be cancelled because its payment is '{Status}'.");
        }
        AuthorizationStatus = "VOIDED";
        CancelledAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Cancelled;
    }

    /// <summary>Finds an already-recorded refund for the caller's idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Validates and stages a new refund. Guarantees a partly-refunded order never becomes
    /// refundable beyond what was captured.
    /// </summary>
    public PaymentRefund AddRefund(string idempotencyKey, decimal amount)
    {
        if (Status != PaymentStatus.Fulfilled && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException(
                $"Order {OrderId} cannot be refunded because its payment is '{Status}'.");
        }

        if (amount > RefundableRemaining)
        {
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} {CurrencyCode} exceeds the refundable remaining of {RefundableRemaining:0.00} {CurrencyCode} on order {OrderId}.");
        }

        var refund = new PaymentRefund(idempotencyKey, amount, CurrencyCode);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>Recomputes the payment status after a refund result is known.</summary>
    public void RecalculateAfterRefund()
    {
        if (RefundedAmount <= 0m) return;
        Status = RefundedAmount >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }
}
