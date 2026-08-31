using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Records the PayPal payment lifecycle for an order: the authorization (hold),
/// the capture at fulfilment, and any refunds. Carries every PayPal-owned id and
/// status needed for a later request to act on the payment.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    public static class Statuses
    {
        public const string Created = "Created";
        public const string Authorized = "Authorized";
        public const string Captured = "Captured";
        public const string Voided = "Voided";
        public const string Failed = "Failed";
    }

    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal authorizedAmount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        Status = Statuses.Created;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Status { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string Currency { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public int? SavedPaymentMethodId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    // Deterministic PayPal-Request-Id values make every PayPal call idempotent:
    // a retry of the same operation replays the original PayPal response. The seed
    // makes them unique to this payment attempt, so a fresh database can never
    // collide with request ids PayPal has seen for a different order.
    public string RequestSeed { get; private set; } = Guid.NewGuid().ToString("N");
    public string CreateRequestId { get; private set; } = string.Empty;
    public string AuthorizeRequestId { get; private set; } = string.Empty;
    public string? CaptureRequestId { get; private set; }
    public int AttemptCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status != PaymentRefund.Statuses.Failed)
        .Sum(r => r.Amount);

    public decimal RemainingRefundable =>
        CapturedAmount.HasValue ? Math.Max(0m, CapturedAmount.Value - TotalRefunded) : 0m;

    public void AssignRequestIds()
    {
        // Called once the entity has a persistent Id, before any PayPal call.
        CreateRequestId = $"eshop-create-order-{Id}-{RequestSeed}";
        AuthorizeRequestId = $"eshop-authorize-{Id}-{RequestSeed}";
    }

    public void MarkOrderCreated(string payPalOrderId, string? cardBrand, string? cardLast4, int? savedPaymentMethodId)
    {
        PayPalOrderId = payPalOrderId;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        SavedPaymentMethodId = savedPaymentMethodId;
    }

    public void MarkAuthorized(string authorizationId, string authorizationStatus)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Status = Statuses.Authorized;
    }

    public void MarkAuthorizationRenewed(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal grossAmount, decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = Statuses.Captured;
    }

    public void MarkVoided()
    {
        Status = Statuses.Voided;
    }

    public void MarkFailed()
    {
        Status = Statuses.Failed;
    }

    public string NextCaptureRequestId()
    {
        AttemptCount++;
        CaptureRequestId = AttemptCount == 1
            ? $"eshop-capture-{Id}-{RequestSeed}"
            : $"eshop-capture-{Id}-{RequestSeed}-retry{AttemptCount}";
        return CaptureRequestId;
    }

    public string NextReauthorizeRequestId()
    {
        return $"eshop-reauthorize-{Id}-{RequestSeed}-{Guid.NewGuid():N}";
    }

    public string VoidRequestId() => $"eshop-void-{Id}-{RequestSeed}";

    public string RefundRequestId(string idempotencyKey)
    {
        var key = idempotencyKey.Length > 64 ? idempotencyKey[..64] : idempotencyKey;
        return $"eshop-refund-{Id}-{RequestSeed}-{key}";
    }

    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new PaymentRefund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }
}
