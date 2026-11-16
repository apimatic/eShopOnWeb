using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Payment state owned by Finix for a single eShop order. Full card data is never stored here.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(
        int orderId,
        string buyerId,
        string currency,
        long amountCents,
        string paymentInstrumentId,
        int? savedPaymentMethodId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));
        Guard.Against.NullOrEmpty(paymentInstrumentId, nameof(paymentInstrumentId));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        AmountCents = amountCents;
        PaymentInstrumentId = paymentInstrumentId;
        SavedPaymentMethodId = savedPaymentMethodId;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public long AmountCents { get; private set; }
    public string PaymentInstrumentId { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? AuthorizationIdempotencyKey { get; private set; }
    public bool IsVoided { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }

    public string? CaptureTransferId { get; private set; }
    public string? CaptureTransferState { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public decimal? FeeCents { get; private set; }
    public decimal? NetCents { get; private set; }
    public bool FeePending { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void RecordAuthorization(string authorizationId, string idempotencyKey, string? state, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        AuthorizationId = authorizationId;
        AuthorizationIdempotencyKey = idempotencyKey;
        AuthorizationState = state;
        AuthorizationExpiresAt = expiresAt;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void RecordVoid(string? state)
    {
        IsVoided = true;
        VoidedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(state))
        {
            AuthorizationState = state;
        }
        Touch();
    }

    public void RecordCapture(string transferId, long capturedAmountCents, string? state)
    {
        Guard.Against.NullOrEmpty(transferId, nameof(transferId));
        Guard.Against.NegativeOrZero(capturedAmountCents, nameof(capturedAmountCents));

        CaptureTransferId = transferId;
        CapturedAmountCents = capturedAmountCents;
        CaptureTransferState = state;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void RecordCaptureState(string? state)
    {
        CaptureTransferState = state;
        Touch();
    }

    public void RecordFees(decimal feeCents, decimal netCents)
    {
        FeeCents = feeCents;
        NetCents = netCents;
        FeePending = false;
        Touch();
    }

    public void MarkFeePending()
    {
        FeePending = true;
        Touch();
    }

    public void RecordFailure(string? code, string? message)
    {
        FailureCode = code;
        FailureMessage = message;
        Touch();
    }

    public void ClearFailure()
    {
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
