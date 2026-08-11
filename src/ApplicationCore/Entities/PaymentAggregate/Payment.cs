using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for a single <see cref="OrderAggregate.Order"/>. It carries enough of the state
/// PayPal owns — the ids and current status of the hold (authorization), the capture, and the
/// refunds — that a later request can act on it, not only the one that started it.
///
/// One payment maps to exactly one order (1:1). It is a separate aggregate so the money-movement
/// state lives alongside, not inside, the original order model.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>The owning shopper's identity (username), copied from the order for ownership checks.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total to authorize/capture, in <see cref="Currency"/>.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public PaymentState State { get; private set; } = PaymentState.Pending;

    // --- PayPal-owned state for the hold (authorization) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- PayPal-owned state for the capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<Refund> _refunds = new List<Refund>();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
    }

    /// <summary>True once a live hold exists that money can be captured against.</summary>
    public bool IsAuthorized => State == PaymentState.Authorized && AuthorizationId is not null;

    /// <summary>Records the hold PayPal placed (authorization) for this payment.</summary>
    public void SetAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        State = PaymentState.Authorized;
    }

    /// <summary>Replaces the authorization id/status after a reauthorization renewed a stale hold.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (State != PaymentState.Authorized)
        {
            throw new InvalidOrderStateException($"Cannot renew a hold for a payment in state {State}.");
        }
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records the capture PayPal settled, including the fee and net proceeds it reported.</summary>
    public void SetCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        State = PaymentState.Captured;
    }

    /// <summary>Marks the hold as released (voided) without any money moving.</summary>
    public void Void()
    {
        if (State != PaymentState.Authorized)
        {
            throw new InvalidOrderStateException($"Cannot release a hold for a payment in state {State}.");
        }
        AuthorizationStatus = "VOIDED";
        State = PaymentState.Voided;
    }

    /// <summary>The total already refunded (across all recorded refunds).</summary>
    public decimal RefundedAmount() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the capture is still available to refund.</summary>
    public decimal RefundableAmount() => (CapturedAmount ?? 0m) - RefundedAmount();

    /// <summary>Returns a refund already recorded under this idempotency key, if any.</summary>
    public Refund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Records a refund against the capture. Enforces that a partly-refunded order never becomes
    /// refundable beyond what was captured.
    /// </summary>
    public Refund AddRefund(string idempotencyKey, string payPalRefundId, decimal amount, string currency, string status)
    {
        if (State != PaymentState.Captured && State != PaymentState.PartiallyRefunded)
        {
            throw new InvalidOrderStateException("Only a captured payment can be refunded.");
        }

        var refund = new Refund(idempotencyKey, payPalRefundId, amount, currency, status);
        _refunds.Add(refund);

        State = RefundedAmount() >= (CapturedAmount ?? 0m)
            ? PaymentState.Refunded
            : PaymentState.PartiallyRefunded;

        return refund;
    }
}
