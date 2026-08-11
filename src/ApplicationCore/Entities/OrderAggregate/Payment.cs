using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-owned state of an order's payment. Part of the Order aggregate (one payment per order).
/// It carries enough of what PayPal owns — the ids and current status of the hold, the capture and the
/// refunds — that a later request can act on the payment, not just the request that created it.
/// No card details are ever stored here.
/// </summary>
public class Payment : BaseEntity
{
    public const string PayPalProvider = "PayPal";

    public string Provider { get; private set; } = PayPalProvider;
    public string Currency { get; private set; }

    /// <summary>Order total that was authorized, to the cent.</summary>
    public decimal Amount { get; private set; }

    // --- Hold (authorization) ---
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }

    // --- Capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(string payPalOrderId, string authorizationId, string authorizationStatus, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Amount = amount;
        Currency = currency;
    }

    /// <summary>The authorization id/status changed — e.g. it was renewed (reauthorized) before capture.</summary>
    public void UpdateAuthorization(string authorizationId, string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
    }

    /// <summary>Sum of refunds that moved (or will move) money back to the shopper.</summary>
    public decimal RefundedAmount => _refunds.Where(r => r.CountsAgainstCapture).Sum(r => r.Amount);

    /// <summary>How much of the capture can still be refunded. Never negative.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - RefundedAmount;

    /// <summary>Returns an existing refund recorded under the same idempotency key, if any.</summary>
    public Refund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Records a refund. A partly-refunded order must never become refundable beyond what was captured,
    /// so the amount is guarded against the remaining refundable balance.
    /// </summary>
    public void AddRefund(Refund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (CaptureId is null)
        {
            throw new InvalidOperationException("Cannot refund a payment that has not been captured.");
        }
        if (refund.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(refund), "Refund amount must be positive.");
        }
        if (refund.CountsAgainstCapture && refund.Amount > RefundableRemaining)
        {
            throw new InvalidOperationException(
                $"Refund of {refund.Amount:0.00} exceeds the refundable remaining balance of {RefundableRemaining:0.00}.");
        }
        _refunds.Add(refund);
    }
}
