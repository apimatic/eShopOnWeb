using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The PayPal-owned money-movement state for an order: the hold (authorization), the capture, and
/// the refunds. Carries enough of PayPal's own identifiers and current status that a later request
/// (fulfil, cancel, refund, reconcile) can act on it, not only the one that started it. One Payment
/// per order.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, string currencyCode, decimal authorizedAmount,
        string paymentMethodKind, string? paymentMethodDescription)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        AuthorizedAmount = authorizedAmount;
        PaymentMethodKind = paymentMethodKind;
        PaymentMethodDescription = paymentMethodDescription;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string CurrencyCode { get; private set; }
    public decimal AuthorizedAmount { get; private set; }

    /// <summary>"card" (one-off) or "saved_card".</summary>
    public string PaymentMethodKind { get; private set; }
    /// <summary>Safe descriptor, e.g. "Visa ending 1111" — never full card details.</summary>
    public string? PaymentMethodDescription { get; private set; }

    // The hold
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // The capture (money taken at fulfilment)
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // The refunds
    public decimal RefundedAmount { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsAuthorized => AuthorizationId is not null;
    public bool IsCaptured => CaptureId is not null;

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string status,
        DateTimeOffset? expiresAt)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Update the authorization after a reauthorization renews a stale hold.</summary>
    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
    }

    public void RecordVoid(string status)
    {
        AuthorizationStatus = status;
    }

    /// <summary>The amount still refundable — never lets refunds exceed what was captured.</summary>
    public decimal RemainingRefundable => (CapturedAmount ?? 0m) - RefundedAmount;

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void RecordRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        _refunds.Add(new PaymentRefund(refundId, amount, status, idempotencyKey));
        RefundedAmount += amount;
    }
}
