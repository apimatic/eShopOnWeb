using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

    private OrderPayment() { }

    public OrderPayment(string currency)
    {
        Currency = currency.ToUpperInvariant();
        InvoiceId = $"ES-{Guid.NewGuid():N}";
        CreateOrderRequestId = Guid.NewGuid().ToString("N");
        AuthorizeRequestId = Guid.NewGuid().ToString("N");
        ReauthorizeRequestId = Guid.NewGuid().ToString("N");
        CaptureRequestId = Guid.NewGuid().ToString("N");
        VoidRequestId = Guid.NewGuid().ToString("N");
    }

    public int OrderId { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.AwaitingPayment;
    public string Currency { get; private set; } = string.Empty;
    public string InvoiceId { get; private set; } = string.Empty;
    public string CreateOrderRequestId { get; private set; } = string.Empty;
    public string AuthorizeRequestId { get; private set; } = string.Empty;
    public string ReauthorizeRequestId { get; private set; } = string.Empty;
    public string CaptureRequestId { get; private set; } = string.Empty;
    public string VoidRequestId { get; private set; } = string.Empty;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? OriginalAuthorizationTime { get; private set; }
    public DateTimeOffset? AuthorizationTime { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CaptureTime { get; private set; }
    public string? PreviousAuthorizationIds { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void RecordPayPalOrder(string id, string status)
    {
        PayPalOrderId = id;
        PayPalOrderStatus = status;
    }

    public void RecordAuthorization(string id, string status, decimal amount,
        DateTimeOffset? createTime, DateTimeOffset? expirationTime)
    {
        AuthorizationId = id;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationTime = createTime ?? DateTimeOffset.UtcNow;
        OriginalAuthorizationTime ??= AuthorizationTime;
        AuthorizationExpirationTime = expirationTime;
        Status = status == "CREATED" ? PaymentStatus.Authorized : PaymentStatus.AuthorizationPending;
    }

    public void RecordReauthorization(string id, string status, decimal amount,
        DateTimeOffset? createTime, DateTimeOffset? expirationTime)
    {
        RecordAuthorization(id, status, amount, createTime, expirationTime);
        ReauthorizeRequestId = Guid.NewGuid().ToString("N");
    }

    public void RecordCapture(string id, string status, decimal amount, decimal? fee,
        decimal? net, DateTimeOffset? createTime)
    {
        CaptureId = id;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = net;
        CaptureTime = createTime ?? DateTimeOffset.UtcNow;
        Status = status == "COMPLETED" ? PaymentStatus.Captured : PaymentStatus.CapturePending;
    }

    public void RecordVoid(string status)
    {
        AuthorizationStatus = status;
        Status = PaymentStatus.Voided;
    }

    public PaymentRefund AddRefund(string idempotencyKey, string paypalRequestId,
        decimal requestedAmount)
    {
        var refund = new PaymentRefund(idempotencyKey, paypalRequestId, requestedAmount);
        _refunds.Add(refund);
        return refund;
    }

    public decimal CompletedRefundAmount() =>
        _refunds.Where(x => x.Status == "COMPLETED").Sum(x => x.Amount);

    public decimal ReservedRefundAmount() =>
        _refunds.Where(x => x.Status is not ("FAILED" or "CANCELLED")).Sum(x => x.Amount);

    public void RefreshRefundStatus()
    {
        var completed = CompletedRefundAmount();
        if (_refunds.Any(x => x.Status == "PENDING"))
        {
            Status = PaymentStatus.RefundPending;
        }
        else if (CapturedAmount.HasValue && completed >= CapturedAmount.Value)
        {
            Status = PaymentStatus.Refunded;
        }
        else if (completed > 0)
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
        else if (CaptureId is not null)
        {
            Status = CaptureStatus == "COMPLETED" ? PaymentStatus.Captured : PaymentStatus.CapturePending;
        }
    }

    public void BeginNewAuthorizationAttempt()
    {
        if (AuthorizationId is not null)
        {
            PreviousAuthorizationIds = string.IsNullOrEmpty(PreviousAuthorizationIds)
                ? AuthorizationId
                : $"{PreviousAuthorizationIds},{AuthorizationId}";
        }

        PayPalOrderId = null;
        PayPalOrderStatus = null;
        AuthorizationId = null;
        AuthorizationStatus = null;
        AuthorizedAmount = null;
        OriginalAuthorizationTime = null;
        AuthorizationTime = null;
        AuthorizationExpirationTime = null;
        CreateOrderRequestId = Guid.NewGuid().ToString("N");
        AuthorizeRequestId = Guid.NewGuid().ToString("N");
        ReauthorizeRequestId = Guid.NewGuid().ToString("N");
        CaptureRequestId = Guid.NewGuid().ToString("N");
        VoidRequestId = Guid.NewGuid().ToString("N");
        Status = PaymentStatus.AwaitingPayment;
    }
}
