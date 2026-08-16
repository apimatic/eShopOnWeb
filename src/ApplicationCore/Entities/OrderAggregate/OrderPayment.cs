using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Holds the money-movement state for an order. It deliberately carries enough of the state that
/// PayPal owns — the ids and current status for the hold (authorization), the capture and the
/// refunds — that a later request can act on the payment, not only the one that created it.
///
/// Part of the Order aggregate; mutated only through <see cref="Order"/> behaviour methods.
/// No card number is ever stored here; only a safe display summary (e.g. "Visa ending 1111").
/// </summary>
public class OrderPayment : BaseEntity
{
    private readonly List<OrderRefund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    internal OrderPayment(string payPalOrderId, decimal amount, string currencyCode, string? payPalCustomId)
    {
        PayPalOrderId = Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Amount = Guard.Against.NegativeOrZero(amount, nameof(amount));
        CurrencyCode = Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        PayPalCustomId = payPalCustomId;
        Status = PaymentStatus.Pending;
    }

    /// <summary>The PayPal Orders v2 order id that the authorization/capture live under.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>The exact <c>custom_id</c> stamped on the PayPal transaction, for reconciliation matching.</summary>
    public string? PayPalCustomId { get; private set; }

    /// <summary>The order total that was authorized, to the cent.</summary>
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    public PaymentStatus Status { get; private set; }

    // --- Authorization (the hold) ---
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture (the money actually taken) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }

    /// <summary>The fee PayPal charged on the capture, as PayPal reported it.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>The net proceeds to the merchant after PayPal's fee, as PayPal reported it.</summary>
    public decimal? NetAmount { get; private set; }

    // --- Refunds ---
    public decimal RefundedAmount { get; private set; }
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Safe, non-sensitive description of the instrument used (e.g. "Visa ending 1111").</summary>
    public string? InstrumentSummary { get; private set; }

    /// <summary>If a saved (vaulted) card was used, the PayPal vault token id it was paid with.</summary>
    public string? VaultId { get; private set; }

    internal void SetAuthorized(string authorizationId, string? status, DateTimeOffset? expiresAt,
        string? instrumentSummary, string? vaultId)
    {
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        if (!string.IsNullOrEmpty(instrumentSummary)) InstrumentSummary = instrumentSummary;
        if (!string.IsNullOrEmpty(vaultId)) VaultId = vaultId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records a renewed (re-authorized) hold that replaces a stale one.</summary>
    internal void RenewAuthorization(string authorizationId, string? status, DateTimeOffset? expiresAt)
    {
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    internal void SetCaptured(string captureId, string? status, decimal capturedAmount, decimal? fee, decimal? net)
    {
        CaptureId = Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    internal void SetVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    /// <summary>The amount that can still be refunded without exceeding what was captured.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - RefundedAmount;

    internal OrderRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new OrderRefund(payPalRefundId, amount, CurrencyCode, status, idempotencyKey);
        _refunds.Add(refund);
        RefundedAmount += amount;
        Status = RefundedAmount >= (CapturedAmount ?? 0m) ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
