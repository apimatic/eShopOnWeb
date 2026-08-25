using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Tracks the PayPal-side state (authorization, capture, refunds) for a single Order's payment.
/// </summary>
public class Payment : BaseEntity
{
    public const string RefundStatusCompleted = "COMPLETED";

    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(string payPalOrderId, string authorizationId, string authorizationStatus,
        decimal authorizedAmount, string currency, DateTimeOffset? authorizationExpiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizationExpiresAt = authorizationExpiresAt;
    }

    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Where(r => r.Status == RefundStatusCompleted).Sum(r => r.Amount);
    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void Reauthorized(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void Voided(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        AuthorizationStatus = status;
    }

    public void Captured(string captureId, string status, decimal capturedAmount, decimal? feeAmount, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFeeAmount = feeAmount;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public Refund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public Refund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new Refund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }
}
