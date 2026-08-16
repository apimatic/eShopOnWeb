using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The money movement for an <see cref="Order"/>. It holds enough of the state PayPal owns
/// (order id, authorization id/status, capture id/status, refund records) that a later
/// request can act on the payment, not only the one that started it.
/// </summary>
public class Payment : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(decimal amount, string currencyCode)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = PaymentStatus.Pending;
    }

    /// <summary>The order total to authorize/capture — the amount PayPal must hold, to the cent.</summary>
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    public PaymentStatus Status { get; private set; }

    // --- PayPal-owned state ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }

    /// <summary>Invoice id sent to PayPal (stable per order) — used to line up reconciliation records.</summary>
    public string? InvoiceId { get; private set; }

    // --- What PayPal reported at capture ---
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Safe, human-readable description of how the order was paid (never card details).</summary>
    public string? PaymentSourceDescription { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Where(r => r.CountsAgainstCapture).Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public void AssignInvoiceId(string invoiceId) => InvoiceId ??= invoiceId;

    public void SetAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, string? sourceDescription)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        PaymentSourceDescription = sourceDescription;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replace the authorization after a stale one has been renewed (reauthorized).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
    }

    public void SetCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void SetVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Records a refund and advances the payment status. Guards the core invariant:
    /// total refunded can never exceed the captured amount.
    /// </summary>
    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount > RefundableRemaining)
        {
            throw new InvalidOperationException(
                $"Refund of {amount} exceeds the remaining refundable amount of {RefundableRemaining}.");
        }

        var refund = new PaymentRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = TotalRefunded >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }
}
