using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The Finix-owned state of a payment for one order. Full card data is never
/// stored here; only Finix resource identifiers and reported amounts.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string currency)
    {
        OrderId = orderId;
        Currency = currency;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; }

    public string? FinixIdentityId { get; private set; }
    public string? PaymentInstrumentId { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationIdempotencyId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int AuthorizedAmountCents { get; private set; }

    public string? CaptureTransferId { get; private set; }
    public string? CaptureState { get; private set; }
    public int CapturedAmountCents { get; private set; }
    public int FeeCents { get; private set; }
    public int NetCents { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<RefundRecord> _refunds = new();
    public IReadOnlyCollection<RefundRecord> Refunds => _refunds.AsReadOnly();

    [NotMapped]
    public int RefundedAmountCents =>
        _refunds.Where(r => !string.Equals(r.State, "FAILED", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(r.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.AmountCents);

    [NotMapped]
    public int RefundableAmountCents => Math.Max(0, CapturedAmountCents - RefundedAmountCents);

    public void RecordAuthorization(
        string? identityId,
        string paymentInstrumentId,
        string authorizationId,
        string state,
        int amountCents,
        DateTimeOffset? expiresAt)
    {
        FinixIdentityId = identityId;
        PaymentInstrumentId = paymentInstrumentId;
        AuthorizationId = authorizationId;
        AuthorizationState = state;
        AuthorizedAmountCents = amountCents;
        AuthorizationExpiresAt = expiresAt;
        FailureCode = null;
        FailureMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordAuthorizationFailure(string? code, string? message)
    {
        FailureCode = code;
        FailureMessage = message;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void EnsureAuthorizationIdempotencyId(string idempotencyId)
    {
        AuthorizationIdempotencyId ??= idempotencyId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ClearAuthorizationIdempotencyId()
    {
        AuthorizationIdempotencyId = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordVoid(string state)
    {
        AuthorizationState = state;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCapture(
        string transferId,
        string state,
        int capturedAmountCents,
        int feeCents,
        int netCents)
    {
        CaptureTransferId = transferId;
        CaptureState = state;
        CapturedAmountCents = capturedAmountCents;
        FeeCents = feeCents;
        NetCents = netCents;
        CapturedAt = DateTimeOffset.UtcNow;
        FailureCode = null;
        FailureMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RefreshCapture(string state, int capturedAmountCents, int feeCents)
    {
        CaptureState = state;
        CapturedAmountCents = capturedAmountCents;
        FeeCents = feeCents;
        NetCents = capturedAmountCents - feeCents;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public RefundRecord AddRefund(string idempotencyKey, int amountCents)
    {
        var refund = new RefundRecord(Id, idempotencyKey, amountCents);
        _refunds.Add(refund);
        UpdatedAt = DateTimeOffset.UtcNow;
        return refund;
    }
}
