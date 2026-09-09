using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money-movement state for a single <see cref="OrderAggregate.Order"/>. Modelled as its
/// own aggregate (keyed by <see cref="OrderId"/>) so the existing Order model is untouched, and
/// carries enough of the state PayPal owns — the ids and current status of the hold
/// (authorization), the capture and every refund — that a later request can act on it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        // A globally-unique invoice id (some PayPal accounts require invoice ids be unique across
        // all transactions). Stamped on the PayPal order and used to reconcile it back to eShop.
        PayPalInvoiceId = $"eshop-order-{orderId}-{Guid.NewGuid():N}";
    }

    public int OrderId { get; private set; }

    /// <summary>When the payment (order) was created; used to scope reconciliation to a range.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The invoice id stamped on the PayPal order, used to line up reconciliation.</summary>
    public string PayPalInvoiceId { get; private set; }

    /// <summary>Owner of the order/payment; used to scope every shopper action to its caller.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Order total to authorize/capture, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO-4217 currency code, from configuration.</summary>
    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- State PayPal owns ---

    /// <summary>The v2 checkout order id created when authorizing.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>The authorization (hold) id.</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>PayPal's current status for the hold, e.g. CREATED / CAPTURED / VOIDED / EXPIRED.</summary>
    public string? AuthorizationStatus { get; private set; }

    /// <summary>The capture id, once the money has been taken at fulfilment.</summary>
    public string? CaptureId { get; private set; }

    /// <summary>PayPal's current status for the capture, e.g. COMPLETED.</summary>
    public string? CaptureStatus { get; private set; }

    /// <summary>Amount PayPal actually captured.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>Fee PayPal reported for the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant PayPal reported for the capture.</summary>
    public decimal? NetAmount { get; private set; }

    /// <summary>Safe display of the card used (brand + last four); never full details.</summary>
    public string? CardBrand { get; private set; }
    public string? CardLastFour { get; private set; }

    /// <summary>Operator-actionable reason when a payment step could not complete.</summary>
    public string? FailureReason { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Records the hold placed at <c>pay</c> time.</summary>
    public void SetAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        string? cardBrand, string? cardLastFour)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        CardBrand = cardBrand;
        CardLastFour = cardLastFour;
        FailureReason = null;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Updates the hold's id/status after a reauthorization renews a stale hold.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
    }

    public void SetAuthorizationStatus(string authorizationStatus) => AuthorizationStatus = authorizationStatus;

    /// <summary>Records the money taken at fulfilment together with what PayPal reported.</summary>
    public void SetCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? fee, decimal? net)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        AuthorizationStatus = "CAPTURED";
        FailureReason = null;
        Status = PaymentStatus.Captured;
    }

    /// <summary>Refreshes the fee/net breakdown once PayPal has settled the capture.</summary>
    public void SetCaptureBreakdown(decimal? fee, decimal? net)
    {
        if (fee.HasValue) PayPalFee = fee;
        if (net.HasValue) NetAmount = net;
    }

    public void SetVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public void SetFailed(string reason)
    {
        FailureReason = reason;
        Status = PaymentStatus.Failed;
    }

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        RecomputeRefundState();
    }

    private void RecomputeRefundState()
    {
        var captured = CapturedAmount ?? 0m;
        var refunded = TotalRefunded();
        if (refunded <= 0m)
        {
            return;
        }
        Status = refunded >= captured ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }

    /// <summary>Sum of all refunds taken so far.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the capture may still be refunded — never below zero.</summary>
    public decimal RefundableRemaining()
    {
        var remaining = (CapturedAmount ?? 0m) - TotalRefunded();
        return remaining < 0m ? 0m : remaining;
    }

    /// <summary>Looks up an already-processed refund by its caller-supplied idempotency key.</summary>
    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
