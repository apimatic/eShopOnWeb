using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Carries the payment state that PayPal owns for an order: the ids and current status of the
/// hold (authorization), the capture, and any refunds. It holds enough state that a later
/// request (fulfil, cancel, refund) can act on the payment, not only the one that started it.
/// Part of the <see cref="Order"/> aggregate — never full card details are stored here.
/// </summary>
public class OrderPayment : BaseEntity
{
    private readonly List<OrderRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(
        string payPalOrderId,
        string invoiceId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt,
        decimal authorizedAmount,
        string currency,
        string cardDescription)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        CardDescription = cardDescription;
    }

    /// <summary>The PayPal v2 checkout order id created when authorizing.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>Merchant invoice id sent to PayPal; used to reconcile against transaction reports.</summary>
    public string InvoiceId { get; private set; }

    public decimal AuthorizedAmount { get; private set; }
    public string Currency { get; private set; }

    /// <summary>Safe, non-sensitive description of the funding card, e.g. "VISA ending 1111".</summary>
    public string CardDescription { get; private set; }

    // Hold (authorization)
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // Capture (settlement)
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public int OrderId { get; private set; }

    /// <summary>Whether the current hold has expired and needs renewal before capture.</summary>
    public bool IsAuthorizationExpired(DateTimeOffset now) =>
        AuthorizationExpiresAt.HasValue && now >= AuthorizationExpiresAt.Value;

    /// <summary>Replaces the hold details after a reauthorization renews a stale hold.</summary>
    public void RenewAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkAuthorizationVoided() => AuthorizationStatus = "VOIDED";

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
    }

    /// <summary>Sum of every refund recorded against the capture (excluding failed/cancelled ones).</summary>
    public decimal TotalRefunded() =>
        _refunds.Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.Amount);

    /// <summary>How much of the capture is still available to refund.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(OrderRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        CaptureStatus = RefundableRemaining() <= 0m ? "REFUNDED" : "PARTIALLY_REFUNDED";
    }
}
