using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal payment attached to an <see cref="Order"/>. Holds the state PayPal owns
/// (the hold/authorization, the capture, and the refunds) so later requests can act on it.
/// Persisted as an owned entity of the Order aggregate.
/// </summary>
public class OrderPayment
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(string currency, decimal amount, string invoiceId)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Currency = currency;
        Amount = amount;
        InvoiceId = invoiceId;
    }

    /// <summary>The amount authorized (equals the order total, to the cent).</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    /// <summary>The PayPal invoice_id used for this payment (globally unique; maps back to the order).</summary>
    public string InvoiceId { get; private set; }

    // ----- Hold (authorization) -----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // ----- Capture -----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // ----- Refunds -----
    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Sum of refunds that count against the capture (excludes failed/cancelled).</summary>
    public decimal RefundedAmount => _refunds.Where(r => r.CountsAgainstCapture).Sum(r => r.Amount);

    /// <summary>How much of the captured amount is still refundable.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - RefundedAmount;

    public bool IsAuthorized => !string.IsNullOrEmpty(AuthorizationId);
    public bool IsCaptured => !string.IsNullOrEmpty(CaptureId);

    public void SetAuthorization(string payPalOrderId, string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void UpdateAuthorizationStatus(string status) => AuthorizationStatus = status;

    public void SetCapture(string captureId, string status, decimal capturedAmount, decimal payPalFee, decimal netAmount, DateTimeOffset capturedAt)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public OrderRefund AddRefund(string refundId, string idempotencyKey, decimal amount, string status, DateTimeOffset createdAt)
    {
        var refund = new OrderRefund(refundId, idempotencyKey, amount, status, createdAt);
        _refunds.Add(refund);
        return refund;
    }
}
