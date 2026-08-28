using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentAuthorization> _authorizations = new();
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, decimal amount, string currency, string authorizationRequestId,
        int? savedPaymentMethodId)
    {
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        AuthorizationRequestId = authorizationRequestId;
        SavedPaymentMethodId = savedPaymentMethodId;
    }

    public int OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string AuthorizationRequestId { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public IReadOnlyCollection<PaymentAuthorization> Authorizations => _authorizations.AsReadOnly();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public PaymentAuthorization? CurrentAuthorization => _authorizations.SingleOrDefault(x => x.IsCurrent);
    public decimal RefundedAmount => _refunds.Where(x => x.Status == "COMPLETED").Sum(x => x.Amount);
    public decimal ReservedRefundAmount => _refunds.Where(x => x.Status is "PENDING" or "COMPLETED").Sum(x => x.Amount);

    public void RecordAuthorization(string payPalOrderId, string payPalOrderStatus, string authorizationId,
        string status, decimal amount, DateTimeOffset createdAt, DateTimeOffset? expiresAt, bool isReauthorization)
    {
        foreach (var authorization in _authorizations.Where(x => x.IsCurrent))
        {
            authorization.MakeHistorical();
        }

        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        _authorizations.Add(new PaymentAuthorization(authorizationId, status, amount, Currency, createdAt,
            expiresAt, isReauthorization));
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateAuthorizationStatus(string status)
    {
        CurrentAuthorization?.UpdateStatus(status);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordCapture(string captureId, string status, string requestId, decimal capturedAmount,
        decimal payPalFee, decimal netAmount, DateTimeOffset capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CaptureRequestId = requestId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        CurrentAuthorization?.UpdateStatus("CAPTURED");
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund ReserveRefund(string idempotencyKey, string payPalRequestId, decimal amount)
    {
        var refund = new PaymentRefund(idempotencyKey, payPalRequestId, amount, Currency);
        _refunds.Add(refund);
        UpdatedAt = DateTimeOffset.UtcNow;
        return refund;
    }
}

public class PaymentAuthorization : BaseEntity
{
#pragma warning disable CS8618
    private PaymentAuthorization() { }
#pragma warning restore CS8618

    internal PaymentAuthorization(string payPalId, string status, decimal amount, string currency,
        DateTimeOffset createdAt, DateTimeOffset? expiresAt, bool isReauthorization)
    {
        PayPalId = payPalId;
        Status = status;
        Amount = amount;
        Currency = currency;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        IsReauthorization = isReauthorization;
    }

    public int OrderPaymentId { get; private set; }
    public string PayPalId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public bool IsReauthorization { get; private set; }
    public bool IsCurrent { get; private set; } = true;

    internal void MakeHistorical() => IsCurrent = false;
    internal void UpdateStatus(string status) => Status = status;
}

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618
    private PaymentRefund() { }
#pragma warning restore CS8618

    internal PaymentRefund(string idempotencyKey, string payPalRequestId, decimal amount, string currency)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRequestId = payPalRequestId;
        Amount = amount;
        Currency = currency;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string PayPalRequestId { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = "PENDING";
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void Complete(string payPalRefundId, string status)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Fail()
    {
        Status = "FAILED";
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
