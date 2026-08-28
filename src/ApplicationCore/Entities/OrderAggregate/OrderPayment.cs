using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

    #pragma warning disable CS8618
    private OrderPayment() { }

    public OrderPayment(string currency, string invoiceId, string referenceId)
    {
        Currency = Guard.Against.NullOrEmpty(currency).ToUpperInvariant();
        InvoiceId = Guard.Against.NullOrEmpty(invoiceId);
        ReferenceId = Guard.Against.NullOrEmpty(referenceId);
    }

    public PaymentState State { get; private set; } = PaymentState.AwaitingPayment;
    public string Currency { get; private set; }
    public string InvoiceId { get; private set; }
    public string ReferenceId { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? OriginalAuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int ReauthorizationCount { get; private set; }
    public int AuthorizationAttempt { get; private set; }
    public bool AuthorizationAttemptInProgress { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CaptureCreatedAt { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public decimal RefundedAmount => _refunds
        .Where(x => x.Status is "COMPLETED" or "PENDING" or "INITIATED")
        .Sum(x => x.Amount);

    public void RecordPayPalOrder(string id, string status)
    {
        PayPalOrderId = Guard.Against.NullOrEmpty(id);
        PayPalOrderStatus = Guard.Against.NullOrEmpty(status);
        ClearFailure();
    }

    public int BeginAuthorizationAttempt()
    {
        if (AuthorizationAttemptInProgress) return AuthorizationAttempt;
        AuthorizationAttempt++;
        AuthorizationAttemptInProgress = true;
        State = PaymentState.AwaitingPayment;
        PayPalOrderId = null;
        PayPalOrderStatus = null;
        AuthorizationId = null;
        AuthorizationStatus = null;
        AuthorizedAmount = null;
        AuthorizationCreatedAt = null;
        AuthorizationExpiresAt = null;
        ClearFailure();
        return AuthorizationAttempt;
    }

    public void RecordAuthorization(string id, string status, decimal amount,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt, bool reauthorized = false)
    {
        AuthorizationId = Guard.Against.NullOrEmpty(id);
        AuthorizationStatus = Guard.Against.NullOrEmpty(status);
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        OriginalAuthorizationCreatedAt ??= AuthorizationCreatedAt;
        AuthorizationExpiresAt = expiresAt;
        State = status == "CREATED" ? PaymentState.Authorized : PaymentState.Failed;
        AuthorizationAttemptInProgress = false;
        if (reauthorized) ReauthorizationCount++;
        ClearFailure();
    }

    public void SynchronizeAuthorization(string status, DateTimeOffset? expiresAt)
    {
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt ?? AuthorizationExpiresAt;
    }

    public void RecordCapture(string id, string status, decimal amount, decimal? fee, decimal? net,
        DateTimeOffset? createdAt)
    {
        CaptureId = Guard.Against.NullOrEmpty(id);
        CaptureStatus = Guard.Against.NullOrEmpty(status);
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = net;
        CaptureCreatedAt = createdAt ?? CaptureCreatedAt ?? DateTimeOffset.UtcNow;
        State = status == "COMPLETED" ? PaymentState.Captured : PaymentState.CapturePending;
        ClearFailure();
    }

    public void RecordVoid(string status)
    {
        AuthorizationStatus = status;
        State = PaymentState.Voided;
        ClearFailure();
    }

    public PaymentRefund StartRefund(Guid refundId, string idempotencyKey, decimal amount)
    {
        var refund = new PaymentRefund(refundId, idempotencyKey, amount, Currency);
        _refunds.Add(refund);
        return refund;
    }

    public PaymentRefund? FindRefund(string idempotencyKey) =>
        _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);

    public void RefreshRefundState()
    {
        var refunded = _refunds.Where(x => x.Status is "COMPLETED" or "PENDING").Sum(x => x.Amount);
        if (CapturedAmount.HasValue && refunded >= CapturedAmount.Value)
        {
            State = PaymentState.Refunded;
        }
        else if (refunded > 0)
        {
            State = PaymentState.PartiallyRefunded;
        }
    }

    public void RecordFailure(string code, string message)
    {
        FailureCode = code;
        FailureMessage = message;
        if (State == PaymentState.AwaitingPayment) State = PaymentState.Failed;
        AuthorizationAttemptInProgress = false;
    }

    private void ClearFailure()
    {
        FailureCode = null;
        FailureMessage = null;
    }
}
