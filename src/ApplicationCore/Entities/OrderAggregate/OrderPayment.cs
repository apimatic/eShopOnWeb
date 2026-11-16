using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        PaymentKey = Guid.NewGuid().ToString("N");
        Status = PaymentStatus.AwaitingPayment;
    }

    public int OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string PaymentKey { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public bool AuthorizationIsVoid { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PaymentInstrumentId { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    public string? CaptureTransferId { get; private set; }
    public string? CaptureState { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? Fee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public decimal RefundedAmount { get; private set; }
    public string? FailureMessage { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundableAmount => Math.Max(0m, (CapturedAmount ?? 0m) - RefundedAmount);

    public void MarkAuthorized(string authorizationId, string? state, bool isVoid, DateTimeOffset? expiresAt,
        string paymentInstrumentId, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(paymentInstrumentId, nameof(paymentInstrumentId));

        AuthorizationId = authorizationId;
        AuthorizationState = state;
        AuthorizationIsVoid = isVoid;
        AuthorizationExpiresAt = expiresAt;
        PaymentInstrumentId = paymentInstrumentId;
        SavedPaymentMethodId = savedPaymentMethodId;
        AuthorizedAt = DateTimeOffset.UtcNow;
        FailureMessage = null;
        Status = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationFailed(string? state, string? failureMessage)
    {
        AuthorizationState = state;
        FailureMessage = failureMessage;
        Status = PaymentStatus.AuthorizationFailed;
    }

    public void MarkCancelled(string? state)
    {
        AuthorizationState = state;
        CancelledAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Cancelled;
    }

    public void MarkCaptured(string transferId, string? state, decimal capturedAmount, decimal fee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(transferId, nameof(transferId));

        CaptureTransferId = transferId;
        CaptureState = state;
        CapturedAmount = capturedAmount;
        Fee = fee;
        NetAmount = netAmount;
        FulfilledAt ??= DateTimeOffset.UtcNow;
        RefreshStatus();
    }

    public void UpdateCapture(string? state, decimal capturedAmount, decimal fee, decimal netAmount)
    {
        CaptureState = state;
        CapturedAmount = capturedAmount;
        Fee = fee;
        NetAmount = netAmount;
        RefreshStatus();
    }

    public void UpdateAuthorization(string? state, bool isVoid)
    {
        AuthorizationState = state;
        AuthorizationIsVoid = isVoid;
    }

    public PaymentRefund AddRefund(string finixTransferId, decimal amount, string idempotencyKey,
        string? state, string? type)
    {
        Guard.Against.NullOrEmpty(finixTransferId, nameof(finixTransferId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var refund = new PaymentRefund(Id, finixTransferId, amount, idempotencyKey, state, type);
        _refunds.Add(refund);
        RefundedAmount += amount;
        RefreshStatus();
        return refund;
    }

    public void UpdateRefundState(int refundId, string? state)
    {
        _refunds.FirstOrDefault(r => r.Id == refundId)?.UpdateState(state);
    }

    private void RefreshStatus()
    {
        if (CapturedAmount is null)
            return;

        Status = RefundedAmount <= 0m
            ? PaymentStatus.Captured
            : RefundedAmount >= CapturedAmount.Value
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
    }
}
