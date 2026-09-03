using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

    private OrderPayment() { }

    internal OrderPayment(string currency)
    {
        PaymentReference = Guid.NewGuid().ToString("N");
        Currency = currency;
        Status = OrderPaymentStatus.AwaitingPayment;
    }

    public int OrderId { get; private set; }
    public string PaymentReference { get; private set; } = string.Empty;
    public string Currency { get; private set; } = string.Empty;
    public OrderPaymentStatus Status { get; private set; }
    public string? ProviderOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? ReauthorizedAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public int? PaymentMethodId { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public decimal ReservedRefundAmount => _refunds.Where(x => x.ReservesFunds).Sum(x => x.Amount);

    public void BeginAuthorization(string providerOrderId, int? paymentMethodId)
    {
        if (Status is OrderPaymentStatus.Authorized or OrderPaymentStatus.Captured or
            OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            return;
        }

        if (Status is not OrderPaymentStatus.AwaitingPayment and not OrderPaymentStatus.Authorizing and
            not OrderPaymentStatus.Failed)
        {
            throw new InvalidOperationException($"Payment cannot be authorized while it is {Status}.");
        }

        ProviderOrderId = providerOrderId;
        PaymentMethodId = paymentMethodId;
        Status = OrderPaymentStatus.Authorizing;
    }

    public void RecordAuthorization(string authorizationId, string providerStatus, decimal amount,
        DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = providerStatus;
        AuthorizedAmount = amount;
        AuthorizedAt ??= DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
        Status = providerStatus == "PENDING" ? OrderPaymentStatus.Authorizing : OrderPaymentStatus.Authorized;
    }

    public void RecordPayerActionRequired(string providerOrderId)
    {
        ProviderOrderId = providerOrderId;
        Status = OrderPaymentStatus.PayerActionRequired;
    }

    public void BeginCapture()
    {
        if (Status is OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded or
            OrderPaymentStatus.Refunded)
        {
            return;
        }

        if (Status is not OrderPaymentStatus.Authorized and not OrderPaymentStatus.Capturing)
        {
            throw new InvalidOperationException($"Payment cannot be captured while it is {Status}.");
        }

        Status = OrderPaymentStatus.Capturing;
    }

    public void RecordReauthorization(string authorizationId, string providerStatus, decimal amount,
        DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = providerStatus;
        AuthorizedAmount = amount;
        AuthorizationExpiresAt = expiresAt;
        ReauthorizedAt = DateTimeOffset.UtcNow;
        Status = OrderPaymentStatus.Authorized;
    }

    public void RecordCapture(string captureId, string providerStatus, decimal amount, decimal? fee,
        decimal? net)
    {
        CaptureId = captureId;
        CaptureStatus = providerStatus;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt ??= DateTimeOffset.UtcNow;
        Status = providerStatus == "COMPLETED" ? OrderPaymentStatus.Captured : OrderPaymentStatus.Capturing;
    }

    public void RecordVoid(string providerStatus)
    {
        AuthorizationStatus = providerStatus;
        Status = OrderPaymentStatus.Voided;
    }

    public PaymentRefund BeginRefund(string idempotencyKey, string providerRequestId, decimal amount)
    {
        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (Status is not OrderPaymentStatus.Captured and not OrderPaymentStatus.RefundPending and
            not OrderPaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Payment cannot be refunded while it is {Status}.");
        }

        var captured = CapturedAmount ?? 0m;
        if (amount <= 0m || ReservedRefundAmount + amount > captured)
        {
            throw new InvalidOperationException("Refund amount exceeds the captured amount remaining.");
        }

        var refund = new PaymentRefund(idempotencyKey, providerRequestId, amount);
        _refunds.Add(refund);
        return refund;
    }

    public void RecordRefund(PaymentRefund refund, string providerRefundId, string providerStatus, decimal amount)
    {
        refund.RecordProviderResult(providerRefundId, providerStatus, amount);
        var completed = _refunds.Where(x => x.Status == "COMPLETED").Sum(x => x.Amount);
        if (_refunds.Any(x => x.Status == "PENDING"))
        {
            Status = OrderPaymentStatus.RefundPending;
        }
        else if (completed >= CapturedAmount)
        {
            Status = OrderPaymentStatus.Refunded;
        }
        else if (completed > 0m)
        {
            Status = OrderPaymentStatus.PartiallyRefunded;
        }
        else
        {
            Status = OrderPaymentStatus.Captured;
        }
    }
}
