using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRecord : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

    private PaymentRecord() { }

    public PaymentRecord(decimal orderAmount, string currency)
    {
        ExternalReference = $"eshop-{Guid.NewGuid():N}";
        OrderAmount = orderAmount;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string ExternalReference { get; private set; } = string.Empty;
    public PaymentState State { get; private set; } = PaymentState.AwaitingPayment;
    public decimal OrderAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizationUpdatedAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public string? ProcessorResponseCode { get; private set; }
    public string? ProcessorAvsCode { get; private set; }
    public string? ProcessorCvvCode { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public decimal ReservedRefundAmount => _refunds
        .Where(r => r.Status is not "FAILED" and not "CANCELLED")
        .Sum(r => r.Amount);

    public void RecordPayPalOrder(string id, string status)
    {
        PayPalOrderId = id;
        PayPalOrderStatus = status;
    }

    public void RecordAuthorization(string id, string status, decimal amount,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt, DateTimeOffset? updatedAt,
        string? responseCode, string? avsCode, string? cvvCode)
    {
        AuthorizationId = id;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationUpdatedAt = updatedAt;
        ProcessorResponseCode = responseCode;
        ProcessorAvsCode = avsCode;
        ProcessorCvvCode = cvvCode;
        State = PaymentState.Authorized;
    }

    public void RecordAuthorizationStatus(string status, DateTimeOffset? updatedAt)
    {
        AuthorizationStatus = status;
        AuthorizationUpdatedAt = updatedAt;
    }

    public void RecordCapture(string id, string status, decimal amount, decimal? fee,
        decimal? net, DateTimeOffset? capturedAt, string? responseCode,
        string? avsCode, string? cvvCode)
    {
        CaptureId = id;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = capturedAt;
        ProcessorResponseCode = responseCode;
        ProcessorAvsCode = avsCode;
        ProcessorCvvCode = cvvCode;
        State = PaymentState.Captured;
    }

    public void MarkVoided(string status)
    {
        AuthorizationStatus = status;
        State = PaymentState.Voided;
    }

    public PaymentRefund ReserveRefund(string idempotencyKey, decimal amount)
    {
        var refund = new PaymentRefund(idempotencyKey, amount, Currency);
        _refunds.Add(refund);
        return refund;
    }

    public void RefreshRefundState()
    {
        if (CapturedAmount is null)
        {
            return;
        }

        var refunded = _refunds.Where(r => r.Status == "COMPLETED").Sum(r => r.Amount);
        if (refunded >= CapturedAmount.Value)
        {
            State = PaymentState.Refunded;
            CaptureStatus = "REFUNDED";
        }
        else if (refunded > 0)
        {
            State = PaymentState.PartiallyRefunded;
            CaptureStatus = "PARTIALLY_REFUNDED";
        }
    }
}
