using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment behind one order, carrying the PayPal-owned state (order, authorization, capture and
/// refund ids and statuses) that later requests need to act on it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency, DateTimeOffset now)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.AuthorizationFailed;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }

    /// <summary>Set when a PayPal write failed in a way that leaves its outcome unknown; cleared once settled.</summary>
    public DateTimeOffset? OutcomeUnknownSince { get; private set; }

    /// <summary>Operator-readable explanation of the most recent failure.</summary>
    public string? LastError { get; private set; }

    // Authorization attempt
    public int Attempt { get; private set; }
    public string? InvoiceId { get; private set; }
    public string? CreateOrderRequestId { get; private set; }
    public string? AuthorizeRequestId { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLastDigits { get; private set; }

    // Authorization (hold)
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    /// <summary>When the original hold was placed; renewals do not move it.</summary>
    public DateTimeOffset? FirstAuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? ReauthorizeRequestId { get; private set; }

    // Capture
    public string? CaptureRequestId { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // Void
    public string? VoidRequestId { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsTransitional =>
        Status is PaymentStatus.Authorizing or PaymentStatus.Reauthorizing or PaymentStatus.Capturing or PaymentStatus.Voiding;

    public decimal RefundedAmount => _refunds.Where(r => r.Status == RefundStatus.Succeeded).Sum(r => r.Amount);

    /// <summary>What may still be refunded: captured minus every refund that is not definitively failed.</summary>
    public decimal RefundableAmount => (CapturedAmount ?? 0m) - _refunds.Where(r => r.ReservesFunds).Sum(r => r.Amount);

    public void StartAuthorization(string invoiceId, string createOrderRequestId, string authorizeRequestId,
        int? savedPaymentMethodId, DateTimeOffset now)
    {
        if (Status != PaymentStatus.AuthorizationFailed)
            throw new InvalidOperationException($"Payment for order {OrderId} cannot start a new authorization while {Status}.");

        Attempt++;
        InvoiceId = invoiceId;
        CreateOrderRequestId = createOrderRequestId;
        AuthorizeRequestId = authorizeRequestId;
        SavedPaymentMethodId = savedPaymentMethodId;
        PayPalOrderId = null;
        AuthorizationId = null;
        AuthorizationStatus = null;
        CardBrand = null;
        CardLastDigits = null;
        LastError = null;
        OutcomeUnknownSince = null;
        Status = PaymentStatus.Authorizing;
        UpdatedAt = now;
    }

    public void PayPalOrderCreated(string payPalOrderId, DateTimeOffset now)
    {
        PayPalOrderId = payPalOrderId;
        UpdatedAt = now;
    }

    public void Authorized(string authorizationId, string? authorizationStatus, DateTimeOffset? authorizedAt,
        DateTimeOffset? expiresAt, string? cardBrand, string? cardLastDigits, DateTimeOffset now)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAt = authorizedAt ?? now;
        FirstAuthorizedAt = AuthorizedAt;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand ?? CardBrand;
        CardLastDigits = cardLastDigits ?? CardLastDigits;
        Status = PaymentStatus.Authorized;
        OutcomeUnknownSince = null;
        LastError = null;
        UpdatedAt = now;
    }

    public void AuthorizationFailed(string reason, DateTimeOffset now)
    {
        Status = PaymentStatus.AuthorizationFailed;
        LastError = reason;
        OutcomeUnknownSince = null;
        UpdatedAt = now;
    }

    public void StartReauthorization(string requestId, DateTimeOffset now)
    {
        EnsureStatus(PaymentStatus.Authorized, "renew the authorization");
        ReauthorizeRequestId = requestId;
        Status = PaymentStatus.Reauthorizing;
        UpdatedAt = now;
    }

    public void Reauthorized(string authorizationId, string? authorizationStatus, DateTimeOffset? authorizedAt,
        DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAt = authorizedAt ?? now;
        AuthorizationExpiresAt = expiresAt ?? AuthorizationExpiresAt;
        Status = PaymentStatus.Authorized;
        OutcomeUnknownSince = null;
        LastError = null;
        UpdatedAt = now;
    }

    public void StartCapture(string requestId, DateTimeOffset now)
    {
        EnsureStatus(PaymentStatus.Authorized, "capture");
        CaptureRequestId = requestId;
        Status = PaymentStatus.Capturing;
        UpdatedAt = now;
    }

    public void Captured(string captureId, string? captureStatus, decimal capturedAmount, decimal? fee, decimal? net, DateTimeOffset now)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = now;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
        OutcomeUnknownSince = null;
        LastError = null;
        UpdatedAt = now;
    }

    public void StartVoid(string requestId, DateTimeOffset now)
    {
        EnsureStatus(PaymentStatus.Authorized, "void");
        VoidRequestId = requestId;
        Status = PaymentStatus.Voiding;
        UpdatedAt = now;
    }

    public void Voided(string? authorizationStatus, DateTimeOffset now)
    {
        AuthorizationStatus = authorizationStatus ?? "VOIDED";
        VoidedAt = now;
        Status = PaymentStatus.Voided;
        OutcomeUnknownSince = null;
        LastError = null;
        UpdatedAt = now;
    }

    /// <summary>A capture, void or renewal that PayPal definitively refused: the hold is still in place.</summary>
    public void SettleActionFailed(string reason, string? authorizationStatus, DateTimeOffset now)
    {
        Status = PaymentStatus.Authorized;
        if (authorizationStatus is not null) AuthorizationStatus = authorizationStatus;
        LastError = reason;
        OutcomeUnknownSince = null;
        UpdatedAt = now;
    }

    public void MarkOutcomeUnknown(string reason, DateTimeOffset now)
    {
        OutcomeUnknownSince ??= now;
        LastError = reason;
        UpdatedAt = now;
    }

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, string payPalRequestId, DateTimeOffset now)
    {
        if (Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            throw new InvalidOperationException($"Payment for order {OrderId} cannot be refunded while {Status}.");
        if (amount <= 0 || amount > RefundableAmount)
            throw new InvalidOperationException($"Refund of {amount} exceeds the refundable amount {RefundableAmount}.");

        var refund = new PaymentRefund(idempotencyKey, amount, payPalRequestId, now);
        _refunds.Add(refund);
        UpdatedAt = now;
        return refund;
    }

    public void RefundSucceeded(PaymentRefund refund, string payPalRefundId, string? payPalStatus, DateTimeOffset now)
    {
        refund.Succeeded(payPalRefundId, payPalStatus, now);
        RecomputeRefundState(now);
    }

    public void RefundFailed(PaymentRefund refund, string reason, DateTimeOffset now)
    {
        refund.Failed(reason, now);
        RecomputeRefundState(now);
    }

    public void RefundOutcomeUnknown(PaymentRefund refund, DateTimeOffset now)
    {
        refund.OutcomeUnknown(now);
        UpdatedAt = now;
    }

    private void RecomputeRefundState(DateTimeOffset now)
    {
        var refunded = RefundedAmount;
        if (refunded <= 0) Status = PaymentStatus.Captured;
        else if (refunded >= (CapturedAmount ?? 0m)) Status = PaymentStatus.Refunded;
        else Status = PaymentStatus.PartiallyRefunded;
        UpdatedAt = now;
    }

    private void EnsureStatus(PaymentStatus expected, string action)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Payment for order {OrderId} cannot {action} while {Status}.");
    }
}
