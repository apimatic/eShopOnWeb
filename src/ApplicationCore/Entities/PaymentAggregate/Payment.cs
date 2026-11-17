using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public enum PaymentStatus
{
    AwaitingPayment,
    Authorized,
    Captured,
    PartiallyRefunded,
    Refunded,
    Canceled,
    PaymentFailed
}

public enum FulfilmentStatus
{
    Pending,
    Fulfilled,
    Canceled
}

public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string ownerId, long amountCents, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        OwnerId = ownerId;
        AmountCents = amountCents;
        Currency = currency;
        PublicReference = Guid.NewGuid().ToString("N");
        Status = PaymentStatus.AwaitingPayment;
        FulfilmentStatus = FulfilmentStatus.Pending;
    }

    public int OrderId { get; private set; }
    public string OwnerId { get; private set; }
    public string PublicReference { get; private set; }
    public long AmountCents { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }
    public FulfilmentStatus FulfilmentStatus { get; private set; }

    public string? FinixIdentityId { get; private set; }
    public string? FinixPaymentInstrumentId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationState { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public int AuthorizationAttempt { get; private set; }

    public string? CaptureTransferId { get; private set; }
    public string? CaptureTransferState { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public decimal? FeeCents { get; private set; }
    public decimal? NetCents { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }

    public DateTimeOffset? CanceledAt { get; private set; }
    public string? FailureMessage { get; private set; }

    public void SetCustomerIdentity(string finixIdentityId)
    {
        Guard.Against.NullOrEmpty(finixIdentityId, nameof(finixIdentityId));
        FinixIdentityId = finixIdentityId;
    }

    public void SetPaymentInstrument(string finixPaymentInstrumentId)
    {
        Guard.Against.NullOrEmpty(finixPaymentInstrumentId, nameof(finixPaymentInstrumentId));
        FinixPaymentInstrumentId = finixPaymentInstrumentId;
    }

    public int BeginAuthorizationAttempt()
    {
        AuthorizationAttempt++;
        FailureMessage = null;
        return AuthorizationAttempt;
    }

    public void MarkAuthorized(string authorizationId, string authorizationState, DateTimeOffset authorizedAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationState = authorizationState;
        AuthorizedAt = authorizedAt;
        FailureMessage = null;

        if (authorizationState.Equals("SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            Status = PaymentStatus.Authorized;
        }
        else
        {
            Status = PaymentStatus.PaymentFailed;
        }
    }

    public void MarkAuthorizationFailed(string? failureMessage, string? authorizationId = null, string? authorizationState = null)
    {
        Status = PaymentStatus.PaymentFailed;
        FailureMessage = failureMessage;
        if (!string.IsNullOrWhiteSpace(authorizationId))
        {
            AuthorizationId = authorizationId;
        }
        if (!string.IsNullOrWhiteSpace(authorizationState))
        {
            AuthorizationState = authorizationState;
        }
    }

    public void MarkCaptured(
        string captureTransferId,
        string captureTransferState,
        long capturedAmountCents,
        decimal feeCents,
        decimal netCents,
        DateTimeOffset fulfilledAt)
    {
        CaptureTransferId = captureTransferId;
        CaptureTransferState = captureTransferState;
        CapturedAmountCents = capturedAmountCents;
        FeeCents = feeCents;
        NetCents = netCents;
        FulfilledAt = fulfilledAt;
        FulfilmentStatus = FulfilmentStatus.Fulfilled;
        Status = PaymentStatus.Captured;
        FailureMessage = null;
    }

    public void MarkCaptureFailed(string failureMessage)
    {
        FailureMessage = failureMessage;
    }

    public void MarkCanceled(DateTimeOffset canceledAt, string? authorizationState = null)
    {
        CanceledAt = canceledAt;
        FulfilmentStatus = FulfilmentStatus.Canceled;
        Status = PaymentStatus.Canceled;
        if (!string.IsNullOrWhiteSpace(authorizationState))
        {
            AuthorizationState = authorizationState;
        }
    }

    public void RecordRefund(long totalRefundedCents)
    {
        if (CapturedAmountCents.HasValue && totalRefundedCents >= CapturedAmountCents.Value)
        {
            Status = PaymentStatus.Refunded;
        }
        else if (totalRefundedCents > 0)
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
    }
}
