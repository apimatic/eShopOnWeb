using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Carries the PayPal-owned state of an order's payment: the ids and current status of the hold
/// (authorization), the capture, and the refunds — enough that a later request can act on the
/// payment, not just the one that created it. No full card details are ever stored here.
/// Part of the <see cref="Order"/> aggregate.
/// </summary>
public class OrderPayment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(string payPalOrderId, string authorizationId, string authorizationStatus,
        decimal authorizedAmount, string currency, DateTimeOffset? authorizationExpiresAt,
        string paymentReference, string? savedCardDescriptor)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(paymentReference, nameof(paymentReference));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizationExpiresAt = authorizationExpiresAt;
        PaymentReference = paymentReference;
        SavedCardDescriptor = savedCardDescriptor;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal order id created for the authorization.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>
    /// A unique correlation token stamped onto the PayPal order (custom_id) at authorization time,
    /// used to line this payment up against PayPal's transaction records in reconciliation. Unlike the
    /// order id, it never collides across runs on a shared sandbox account.
    /// </summary>
    public string PaymentReference { get; private set; }

    // --- Authorization (the hold) ---
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset AuthorizedAt { get; private set; }

    /// <summary>A safe label of the saved card used, when the shopper paid with one (never a PAN).</summary>
    public string? SavedCardDescriptor { get; private set; }

    // --- Capture (the money actually taken at fulfilment) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // --- Refunds ---
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>
    /// Replace the authorization details after a stale hold is renewed (re-authorized). The new
    /// authorization has a fresh id, status and honor period.
    /// </summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAuthorizationVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    /// <summary>Record what PayPal reported when the authorization was captured at fulfilment.</summary>
    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund AddRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        var refund = new PaymentRefund(payPalRefundId, idempotencyKey, amount, Currency, status);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>Total refunded so far across all recorded refunds.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>Find a prior refund created under the same idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// The amount still refundable: captured minus already-refunded. Never below zero, and never
    /// lets an order become refundable beyond what was captured.
    /// </summary>
    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - TotalRefunded();
        return remaining > 0m ? remaining : 0m;
    }
}
