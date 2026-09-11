using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Carries the payment state that PayPal owns for an <see cref="Order"/>: the ids and current
/// status of the hold (authorization), the capture, and every refund. Enough state is retained
/// that a later request can act on the payment, not only the one that created it.
///
/// Part of the Order aggregate. Full card details are never stored here.
/// </summary>
public class OrderPayment : BaseEntity
{
    private readonly List<OrderRefund> _refunds = new();

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(string providerOrderId, decimal amount, string currency, string providerReference)
    {
        Guard.Against.NullOrEmpty(providerOrderId, nameof(providerOrderId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(providerReference, nameof(providerReference));

        ProviderOrderId = providerOrderId;
        Amount = amount;
        Currency = currency;
        ProviderReference = providerReference;
    }

    /// <summary>The PayPal order id that contains the hold/capture (the money-movement container).</summary>
    public string ProviderOrderId { get; private set; }

    /// <summary>The reference (invoice/custom id) sent to the provider, used to reconcile the two systems.</summary>
    public string ProviderReference { get; private set; }

    /// <summary>The authorized amount, equal to the order total to the cent.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    // --- Authorization (the hold) ---
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    // --- Capture (the take) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // --- Refunds ---
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public void SetAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAuthorizationVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    public void SetCapture(string captureId, string status, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
    }

    /// <summary>Sum of refunds already recorded against the capture.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the capture is still refundable.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>Returns true if a refund with the supplied idempotency key has already been recorded.</summary>
    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public OrderRefund RecordRefund(string providerRefundId, decimal amount, string status, string idempotencyKey)
    {
        if (CaptureId is null)
        {
            throw new InvalidOperationException("Cannot refund a payment that has not been captured.");
        }

        // A partly-refunded order must never become refundable beyond what was captured.
        if (amount > RefundableRemaining())
        {
            throw new InvalidOperationException(
                $"Refund of {amount} exceeds the refundable remaining amount of {RefundableRemaining()}.");
        }

        var refund = new OrderRefund(providerRefundId, amount, Currency, status, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }
}
