using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Payment : BaseEntity
{
    private readonly List<PaymentAuthorization> _authorizations = new();
    private readonly List<PaymentRefund> _refunds = new();

    private Payment() { }

    public Payment(decimal amount, string currency)
    {
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public PaymentStatus Status { get; private set; } = PaymentStatus.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public IReadOnlyCollection<PaymentAuthorization> Authorizations => _authorizations.AsReadOnly();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public PaymentAuthorization? CurrentAuthorization => _authorizations.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
    public decimal RefundedAmount => _refunds.Where(x => x.Status is "COMPLETED" or "PENDING").Sum(x => x.Amount);

    public void SetPayPalOrder(string paypalOrderId) => PayPalOrderId ??= paypalOrderId;

    public void AddAuthorization(string id, string status, decimal amount, DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        var previous = CurrentAuthorization;
        if (previous?.PayPalAuthorizationId == id)
        {
            previous.UpdateStatus(status);
            return;
        }
        previous?.UpdateStatus("SUPERSEDED");
        _authorizations.Add(new PaymentAuthorization(id, status, amount, createdAt, expiresAt));
        Status = PaymentStatus.Authorized;
    }

    public void MarkVoided(string authorizationStatus)
    {
        CurrentAuthorization?.UpdateStatus(authorizationStatus);
        Status = PaymentStatus.Voided;
    }

    public void RecordCapture(string id, string status, decimal amount, decimal? fee, decimal? net, DateTimeOffset capturedAt)
    {
        CaptureId = id;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = capturedAt;
        CurrentAuthorization?.UpdateStatus("CAPTURED");
        Status = status == "COMPLETED" ? PaymentStatus.Captured : PaymentStatus.CapturePending;
    }

    public PaymentRefund AddRefund(string id, string idempotencyKey, string status, decimal amount, DateTimeOffset createdAt)
    {
        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null) return existing;
        var refund = new PaymentRefund(id, idempotencyKey, status, amount, createdAt);
        _refunds.Add(refund);
        Status = RefundedAmount == CapturedAmount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}
