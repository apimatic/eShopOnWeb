using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal payment that backs a single <see cref="Order"/>. It carries enough of the state that
/// PayPal owns — the ids and current status of the hold (authorization), the capture and each refund —
/// that a later request can act on it, not only the request that created it.
/// One payment per order; keyed by <see cref="OrderId"/>.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, decimal amount, string currency, string authorizeRequestId, string captureRequestId, string invoiceId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        AuthorizeRequestId = authorizeRequestId;
        CaptureRequestId = captureRequestId;
        InvoiceId = invoiceId;
        Status = PaymentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>The order total the shopper must pay, to the cent.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Stable reference recorded on the PayPal order/capture so reconciliation can line them up.</summary>
    public string InvoiceId { get; private set; }

    // --- Idempotency keys (server-generated, stable per operation so a double-click cannot double-charge) ---
    public string AuthorizeRequestId { get; private set; }
    public string CaptureRequestId { get; private set; }

    // --- Hold (authorization) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // --- Refunds ---
    public decimal TotalRefunded { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>The amount still available to refund; a partly-refunded order is never refundable beyond what was captured.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    /// <summary>Updates the pending authorize idempotency key when a new instrument is chosen after a declined attempt.</summary>
    public void SetAuthorizeRequestId(string authorizeRequestId)
    {
        Guard.Against.NullOrEmpty(authorizeRequestId, nameof(authorizeRequestId));
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException("Cannot change the authorize request id after a hold exists.");
        }
        AuthorizeRequestId = authorizeRequestId;
    }

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replaces the authorization after a stale one is renewed (reauthorized) before fulfilment.</summary>
    public void ReplaceAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
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

    public void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        TotalRefunded += refund.Amount;
        Status = RefundableRemaining <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
