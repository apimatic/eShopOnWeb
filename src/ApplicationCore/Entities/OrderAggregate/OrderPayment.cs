using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Payment state for an order. Finix owns the card data; this entity only keeps
/// Finix resource identifiers, amounts and status values.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, long amountCents, string currency)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        AmountCents = amountCents;
        Currency = currency;
        Status = PaymentStatus.PendingAuthorization;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public PaymentStatus Status { get; private set; }
    public long AmountCents { get; private set; }
    public string Currency { get; private set; }

    public string? FinixPaymentInstrumentId { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }

    public string? FinixAuthorizationId { get; private set; }
    public string? FinixAuthorizationState { get; private set; }
    public string? AuthorizationIdempotencyId { get; private set; }
    public bool IsAuthorizationVoided { get; private set; }
    public string? FinixVoidState { get; private set; }

    public string? FinixTransferId { get; private set; }
    public string? FinixTransferState { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public long? FinixFeeCents { get; private set; }
    public long? NetCents { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public long RefundedAmountCents => _refunds.Sum(r => r.AmountCents);

    public long RefundableAmountCents => Math.Max(0, (CapturedAmountCents ?? 0) - RefundedAmountCents);

    public string GetAuthorizationIdempotencyId()
    {
        if (string.IsNullOrWhiteSpace(AuthorizationIdempotencyId))
        {
            AuthorizationIdempotencyId = $"eshop-order-{OrderId}-authorize";
        }

        return AuthorizationIdempotencyId;
    }

    public string PrepareAuthorizationAttempt(DateTimeOffset now)
    {
        AuthorizationIdempotencyId = $"eshop-order-{OrderId}-authorize-{Guid.NewGuid():N}";
        Status = PaymentStatus.PendingAuthorization;
        FailureCode = null;
        FailureMessage = null;
        UpdatedAt = now;
        return AuthorizationIdempotencyId;
    }

    public void MarkAuthorizationPending(string authorizationId, string authorizationState, string paymentInstrumentId,
        int? savedPaymentMethodId, DateTimeOffset now)
    {
        FinixAuthorizationId = authorizationId;
        FinixAuthorizationState = authorizationState;
        FinixPaymentInstrumentId = paymentInstrumentId;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.PendingAuthorization;
        UpdatedAt = now;
    }

    public void MarkAuthorized(string authorizationId, string authorizationState, string paymentInstrumentId,
        int? savedPaymentMethodId, DateTimeOffset now)
    {
        FinixAuthorizationId = authorizationId;
        FinixAuthorizationState = authorizationState;
        FinixPaymentInstrumentId = paymentInstrumentId;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.Authorized;
        FailureCode = null;
        FailureMessage = null;
        UpdatedAt = now;
    }

    public void UpdateAuthorizationState(string authorizationState, bool isVoided, string? voidState, DateTimeOffset now)
    {
        FinixAuthorizationState = authorizationState;
        IsAuthorizationVoided = isVoided;
        FinixVoidState = voidState;
        UpdatedAt = now;
    }

    public void MarkAuthorizationFailed(string? failureCode, string? failureMessage, DateTimeOffset now)
    {
        Status = PaymentStatus.AuthorizationFailed;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        UpdatedAt = now;
    }

    public void MarkCaptured(string transferId, string transferState, long capturedAmountCents,
        long feeCents, long netCents, DateTimeOffset now)
    {
        FinixTransferId = transferId;
        FinixTransferState = transferState;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = feeCents;
        NetCents = netCents;
        Status = PaymentStatus.Captured;
        FailureCode = null;
        FailureMessage = null;
        UpdatedAt = now;
    }

    public void UpdateTransferState(string transferState, long? feeCents, long? netCents, DateTimeOffset now)
    {
        FinixTransferState = transferState;
        if (feeCents.HasValue) FinixFeeCents = feeCents;
        if (netCents.HasValue) NetCents = netCents;
        UpdatedAt = now;
    }

    public void MarkCancelled(string? authorizationState, string? voidState, DateTimeOffset now)
    {
        if (authorizationState != null) FinixAuthorizationState = authorizationState;
        if (voidState != null) FinixVoidState = voidState;
        Status = PaymentStatus.Cancelled;
        UpdatedAt = now;
    }

    public Refund AddRefund(long amountCents, string idempotencyKey, string? finixRefundTransferId,
        string? finixState, DateTimeOffset now)
    {
        var refund = new Refund(amountCents, idempotencyKey, finixRefundTransferId, finixState, now);
        _refunds.Add(refund);
        RefreshRefundStatus(now);
        return refund;
    }

    public void RefreshRefundStatus(DateTimeOffset now)
    {
        var refunded = RefundedAmountCents;
        var captured = CapturedAmountCents ?? 0;
        Status = refunded <= 0
            ? PaymentStatus.Captured
            : refunded >= captured
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
        UpdatedAt = now;
    }
}
