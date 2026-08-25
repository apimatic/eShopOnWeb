using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Tracks the PayPal-owned state for an order's payment: the authorization (hold), the
/// capture taken at fulfilment, and any refunds issued against that capture.
/// </summary>
public class Payment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string currency, decimal amount, string payPalOrderId, string authorizationId,
        string authorizationStatus, string authorizationRequestId, DateTimeOffset? authorizationExpiresOn)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        Guard.Against.NullOrEmpty(authorizationRequestId, nameof(authorizationRequestId));

        OrderId = orderId;
        Currency = currency;
        Amount = amount;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationRequestId = authorizationRequestId;
        AuthorizationExpiresOn = authorizationExpiresOn;
        ReauthorizationCount = 0;
        RefundedAmount = 0m;
        AuthorizedOn = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string Provider { get; private set; } = "PayPal";
    public string Currency { get; private set; }
    public decimal Amount { get; private set; }

    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public string AuthorizationRequestId { get; private set; }
    public DateTimeOffset? AuthorizationExpiresOn { get; private set; }
    public DateTimeOffset AuthorizedOn { get; private set; }
    public int ReauthorizationCount { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedOn { get; private set; }

    public decimal RefundedAmount { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public bool IsCaptured => CaptureId != null;

    public decimal RemainingRefundable => (CapturedAmount ?? 0m) - RefundedAmount;

    public string NextReauthorizationRequestId(int orderId) =>
        $"paypal-reauth-order-{orderId}-{ReauthorizationCount + 1}";

    public void Reauthorize(string authorizationStatus, DateTimeOffset? authorizationExpiresOn)
    {
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresOn = authorizationExpiresOn;
        ReauthorizationCount += 1;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    public void MarkCaptured(string captureId, string captureStatus, string captureRequestId, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));
        Guard.Against.NullOrEmpty(captureRequestId, nameof(captureRequestId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CaptureRequestId = captureRequestId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedOn = DateTimeOffset.UtcNow;
    }

    public void UpdateCaptureStatus(string captureStatus, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));
        CaptureStatus = captureStatus;
        if (payPalFee.HasValue) PayPalFee = payPalFee;
        if (netAmount.HasValue) NetAmount = netAmount;
    }

    public Refund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(Refund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        RefundedAmount += refund.Amount;
    }
}
