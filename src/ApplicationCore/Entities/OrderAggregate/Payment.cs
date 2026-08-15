using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-owned state of an order's payment: the hold (authorization), the capture, and any
/// refunds. It carries enough identifiers and current statuses that a later request (fulfil,
/// cancel, refund, reconcile) can act on it, not only the request that created it.
/// </summary>
public class Payment : BaseEntity
{
    // The PayPal Checkout order id — the top-level handle for the whole payment.
    public string PayPalOrderId { get; private set; }
    public string Currency { get; private set; }

    // Amount held at authorization time; equals the eShop order total to the cent.
    public decimal Amount { get; private set; }

    // --- The hold (authorization) ---
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }

    // --- The capture (taken at fulfilment) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Refunds ---
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(string payPalOrderId, string authorizationId, string authorizationStatus, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Amount = amount;
        Currency = currency;
    }

    /// <summary>Records a fresh authorization id/status after a re-authorization.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
    }

    public void MarkAuthorizationVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    /// <summary>Records what PayPal reported when the authorization was captured at fulfilment.</summary>
    public void RecordCapture(string captureId, string captureStatus, decimal grossAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
    }

    public bool IsCaptured => !string.IsNullOrEmpty(CaptureId);

    /// <summary>Total value already refunded across all recorded refunds.</summary>
    public decimal RefundedAmount => _refunds.Sum(r => r.Amount);

    /// <summary>Value still available to refund against the capture.</summary>
    public decimal RefundableAmount => (CapturedAmount ?? 0m) - RefundedAmount;

    /// <summary>Returns an existing refund with the same idempotency key, if any.</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Guards that <paramref name="amount"/> can still be refunded, then records the refund.
    /// A partly-refunded order can never become refundable beyond what was captured.
    /// </summary>
    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        if (!IsCaptured)
            throw new InvalidOperationException("Cannot refund a payment that has not been captured.");
        if (amount <= 0m)
            throw new InvalidOperationException("Refund amount must be greater than zero.");
        if (amount > RefundableAmount)
            throw new InvalidOperationException(
                $"Refund of {amount:0.00} exceeds the remaining refundable amount of {RefundableAmount:0.00}.");

        var refund = new PaymentRefund(refundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }
}
