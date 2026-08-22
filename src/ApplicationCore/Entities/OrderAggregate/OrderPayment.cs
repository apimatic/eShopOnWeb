using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public static OrderPayment CreatePending(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        return new OrderPayment { Currency = currency };
    }

    public string Currency { get; private set; } = string.Empty;
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public string? AuthorizeRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public string? PayPalInvoiceId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds.Sum(r => r.Amount);

    public decimal RemainingRefundable
    {
        get
        {
            var captured = CapturedAmount ?? 0m;
            var remaining = captured - RefundedAmount;
            return remaining < 0 ? 0 : remaining;
        }
    }

    public bool AuthorizationIsStale(DateTimeOffset utcNow)
    {
        if (!AuthorizationCreatedAt.HasValue)
        {
            return false;
        }

        if (string.Equals(AuthorizationStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (AuthorizationExpiresAt.HasValue && utcNow >= AuthorizationExpiresAt.Value)
        {
            return true;
        }

        // PayPal honor period is three days from the authorization (or last reauthorization).
        return utcNow >= AuthorizationCreatedAt.Value.AddDays(3);
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    internal void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    internal void RecordReauthorization(
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    internal void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal paypalFee,
        decimal netProceeds)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));
        Guard.Against.Negative(capturedAmount, nameof(capturedAmount));
        Guard.Against.Negative(paypalFee, nameof(paypalFee));
        Guard.Against.Negative(netProceeds, nameof(netProceeds));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetProceeds = netProceeds;
        AuthorizationStatus = "CAPTURED";
    }

    internal void SetAuthorizeRequestId(string? requestId) => AuthorizeRequestId = requestId;

    internal void SetCaptureRequestId(string? requestId) => CaptureRequestId = requestId;

    internal void SetPayPalInvoiceId(string? invoiceId) => PayPalInvoiceId = invoiceId;

    internal void RecordVoid(string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        AuthorizationStatus = authorizationStatus;
    }

    internal PaymentRefund RecordRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        var refund = new PaymentRefund(payPalRefundId, idempotencyKey, amount, status);
        _refunds.Add(refund);
        return refund;
    }
}
