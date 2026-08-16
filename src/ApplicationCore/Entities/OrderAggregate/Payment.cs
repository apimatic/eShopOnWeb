using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-backed payment for an <see cref="Order"/>. It carries enough of the state PayPal owns
/// — the ids and current status for the hold (authorization), the capture, and the refunds — that a
/// later request (fulfil, cancel, refund, reconcile) can act on it, not only the request that
/// started it. No card number ever lives here; only a safe descriptor (brand + last four).
/// </summary>
public class Payment : BaseEntity
{
    // Authorization statuses as reported by PayPal (v2 Payments API).
    public const string AuthCreated = "CREATED";
    public const string AuthCaptured = "CAPTURED";
    public const string AuthVoided = "VOIDED";
    public const string AuthExpired = "EXPIRED";
    public const string AuthPending = "PENDING";
    public const string AuthDenied = "DENIED";

    public int OrderId { get; private set; }
    public string Currency { get; private set; }

    /// <summary>
    /// The reference this app sent to PayPal as the purchase unit's custom_id and invoice_id. Globally
    /// unique per order (across app runs), so reconciliation can line a PayPal transaction back to this
    /// exact order without colliding with a different run that happened to reuse order id 1, 2, 3, …
    /// </summary>
    public string PayPalCustomId { get; private set; }

    /// <summary>The amount held at authorization; must equal the order total to the cent.</summary>
    public decimal AuthorizedAmount { get; private set; }

    // --- The hold (authorization) ---
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- The capture (money actually taken at fulfilment) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Safe descriptor of the instrument used (never full card details) ---
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    /// <summary>The saved card used, when this order was paid with a vaulted card (Flow 2).</summary>
    public int? SavedCardId { get; private set; }

    // --- Idempotency keys reused on retries so PayPal never double-charges ---
    public string AuthorizationRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string currency, string payPalCustomId, decimal authorizedAmount, string payPalOrderId,
        string authorizationId, string authorizationStatus, DateTimeOffset? authorizationExpiresAt,
        string authorizationRequestId, string? cardBrand, string? cardLast4, int? savedCardId)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(payPalCustomId, nameof(payPalCustomId));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        Guard.Against.NullOrEmpty(authorizationRequestId, nameof(authorizationRequestId));

        OrderId = orderId;
        Currency = currency;
        PayPalCustomId = payPalCustomId;
        AuthorizedAmount = authorizedAmount;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        AuthorizationRequestId = authorizationRequestId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        SavedCardId = savedCardId;
    }

    /// <summary>Sum of refunds that actually returned (or are returning) money.</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.CountsAgainstBalance).Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded. Never negative.</summary>
    public decimal RefundableRemaining => Math.Max(0m, (CapturedAmount ?? 0m) - TotalRefunded);

    public bool IsCaptured => CaptureId is not null;

    public bool IsAuthorizationActive =>
        string.Equals(AuthorizationStatus, AuthCreated, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the hold can no longer be captured directly and must be renewed first — either it
    /// carries a terminal status or its expiration time has passed.
    /// </summary>
    public bool IsAuthorizationStale(DateTimeOffset now)
    {
        if (string.Equals(AuthorizationStatus, AuthExpired, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!IsAuthorizationActive)
            return true;
        return AuthorizationExpiresAt is { } exp && exp <= now;
    }

    /// <summary>Replace the hold after a reauthorization (PayPal issues a fresh authorization id).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkAuthorizationVoided()
    {
        AuthorizationStatus = AuthVoided;
    }

    public void SetCaptureRequestId(string requestId)
    {
        Guard.Against.NullOrEmpty(requestId, nameof(requestId));
        CaptureRequestId = requestId;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = AuthCaptured;
    }

    public Refund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(Refund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
    }
}
