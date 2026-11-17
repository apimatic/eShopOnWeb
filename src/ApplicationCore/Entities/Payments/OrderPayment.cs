using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payments;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment()
    {
    }
#pragma warning restore CS8618

    public OrderPayment(int orderId, long amountCents, string currency)
    {
        OrderId = orderId;
        AmountAuthorizedCents = amountCents;
        Currency = currency;
        FinixOperationId = Guid.NewGuid().ToString("N");
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string Currency { get; private set; }
    public string FinixOperationId { get; private set; }

    public long AmountAuthorizedCents { get; private set; }
    public long AmountCapturedCents { get; private set; }
    public long AmountRefundedCents { get; private set; }
    public decimal FeeCents { get; private set; }

    public string? FinixAuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public bool AuthorizationVoided { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureTransferId { get; private set; }
    public string? CaptureState { get; private set; }

    public string? FinixPaymentInstrumentId { get; private set; }
    public string? FinixIdentityId { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public long NetCents => AmountCapturedCents - (long)decimal.Round(FeeCents, 0, MidpointRounding.AwayFromZero);
    public long RefundableCents => Math.Max(0, AmountCapturedCents - AmountRefundedCents);

    public void MarkAuthorized(
        string authorizationId,
        string? state,
        long amountCents,
        DateTimeOffset? expiresAt,
        string? paymentInstrumentId,
        string? identityId,
        DateTimeOffset now)
    {
        FinixAuthorizationId = authorizationId;
        AuthorizationState = state;
        AmountAuthorizedCents = amountCents;
        AuthorizationExpiresAt = expiresAt;
        FinixPaymentInstrumentId = paymentInstrumentId;
        FinixIdentityId = identityId;
        Status = PaymentStatus.Authorized;
        FailureCode = null;
        FailureMessage = null;
        AuthorizedAt = now;
        UpdatedAt = now;
    }

    public void MarkAuthorizationFailed(string? failureCode, string? failureMessage, DateTimeOffset now)
    {
        Status = PaymentStatus.Failed;
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        UpdatedAt = now;
    }

    public void UpdateAuthorizationState(string? state, bool isVoid, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        AuthorizationState = state;
        AuthorizationVoided = isVoid;
        AuthorizationExpiresAt = expiresAt;
        UpdatedAt = now;
    }

    public void MarkCaptured(string transferId, string? state, long capturedCents, decimal feeCents, DateTimeOffset now)
    {
        CaptureTransferId = transferId;
        UpdateCaptureDetails(capturedCents, feeCents, state, now);
        Status = PaymentStatus.Captured;
        CapturedAt = now;
        FailureCode = null;
        FailureMessage = null;
    }

    public void UpdateCaptureDetails(long capturedCents, decimal feeCents, string? state, DateTimeOffset now)
    {
        AmountCapturedCents = capturedCents;
        FeeCents = feeCents;
        CaptureState = state;
        UpdatedAt = now;
    }

    public void UpdateCaptureState(string? state, DateTimeOffset now)
    {
        CaptureState = state;
        UpdatedAt = now;
    }

    public void MarkCanceled(DateTimeOffset now)
    {
        Status = PaymentStatus.Canceled;
        CanceledAt = now;
        UpdatedAt = now;
    }

    public void AddRefund(PaymentRefund refund, DateTimeOffset now)
    {
        refund.OrderPaymentId = Id;
        _refunds.Add(refund);
        AmountRefundedCents = _refunds.Sum(r => r.AmountCents);
        Status = AmountRefundedCents >= AmountCapturedCents
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        UpdatedAt = now;
    }
}
