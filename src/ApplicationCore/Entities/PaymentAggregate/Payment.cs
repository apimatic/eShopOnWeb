using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class Payment : BaseEntity, IAggregateRoot
{
    private readonly List<Refund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, long amountCents, string currency)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        AmountCents = amountCents;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public long AmountCents { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? FinixPaymentInstrumentId { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureTransferId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public long? CapturedAmountCents { get; private set; }
    public decimal? FinixFeeCents { get; private set; }
    public decimal? NetProceedsCents { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public long RefundedAmountCents { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public void MarkAuthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt,
        string paymentInstrumentId, int? savedPaymentMethodId)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        FinixPaymentInstrumentId = paymentInstrumentId;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.Authorized;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void MarkFailed(string? failureCode, string? failureMessage)
    {
        FailureCode = failureCode;
        FailureMessage = failureMessage;
        Status = PaymentStatus.Failed;
        Touch();
    }

    public void MarkCaptured(string captureTransferId, string? captureStatus, long capturedAmountCents,
        decimal feeCents, decimal netProceedsCents, DateTimeOffset capturedAt)
    {
        CaptureTransferId = captureTransferId;
        CaptureStatus = captureStatus;
        CapturedAmountCents = capturedAmountCents;
        FinixFeeCents = feeCents;
        NetProceedsCents = netProceedsCents;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
        FailureCode = null;
        FailureMessage = null;
        Touch();
    }

    public void MarkCancelled(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus ?? AuthorizationStatus;
        Status = PaymentStatus.Cancelled;
        Touch();
    }

    public void ApplyRefund(Refund refund)
    {
        _refunds.Add(refund);
        RefundedAmountCents += refund.AmountCents;
        Status = RefundedAmountCents >= (CapturedAmountCents ?? AmountCents)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
    }

    public void SetAuthorizationStatus(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
