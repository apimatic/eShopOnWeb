using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the money movement for a single order: the PayPal ids and current status of the hold
/// (authorization), the capture, and any refunds. This is deliberately kept as a separate aggregate
/// from the reused Order so the existing catalog/basket/order flow is untouched.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    /// <summary>The eShop order this payment settles. One payment per order.</summary>
    public int OrderId { get; private set; }

    /// <summary>Denormalised buyer identity (username/email) so payments can be scoped per shopper.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The three-letter currency the hold/capture is denominated in.</summary>
    public string Currency { get; private set; }

    /// <summary>The amount held at authorization time — equal to the order total to the cent.</summary>
    public decimal AuthorizedAmount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- State owned by PayPal: the hold ---
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- State owned by PayPal: the capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGrossAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // --- Idempotency keys we sent to PayPal, kept stable across retries ---
    public string CreateOrderRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, string currency, decimal authorizedAmount,
        string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt, string createOrderRequestId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        Guard.Against.NullOrEmpty(createOrderRequestId, nameof(createOrderRequestId));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        AuthorizedAmount = authorizedAmount;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        CreateOrderRequestId = createOrderRequestId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>The amount still available to refund against the capture.</summary>
    public decimal RemainingRefundable =>
        (CapturedGrossAmount ?? 0m) - TotalRefunded;

    /// <summary>Sum of refunds that still reserve funds (COMPLETED or PENDING).</summary>
    public decimal TotalRefunded =>
        _refunds.Where(r => r.ReservesFunds).Sum(r => r.Amount);

    /// <summary>Records that the (possibly renewed) hold now points at a fresh authorization.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        if (Status != PaymentStatus.Authorized)
            throw new InvalidPaymentStateException($"Cannot renew a hold that is {Status}.");

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
    }

    public void UpdateAuthorizationStatus(string authorizationStatus, DateTimeOffset? authorizationExpiresAt)
    {
        if (!string.IsNullOrEmpty(authorizationStatus))
            AuthorizationStatus = authorizationStatus;
        if (authorizationExpiresAt.HasValue)
            AuthorizationExpiresAt = authorizationExpiresAt;
    }

    /// <summary>Records the capture reported by PayPal, including the fee/net breakdown.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal grossAmount,
        decimal? payPalFee, decimal? netAmount, string captureRequestId)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));
        Guard.Against.NullOrEmpty(captureRequestId, nameof(captureRequestId));

        if (Status != PaymentStatus.Authorized)
            throw new InvalidPaymentStateException($"Cannot capture a payment that is {Status}.");

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedGrossAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CaptureRequestId = captureRequestId;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
        AuthorizationStatus = "CAPTURED";
    }

    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized)
            throw new InvalidPaymentStateException($"Cannot void a payment that is {Status}.");

        Status = PaymentStatus.Voided;
        AuthorizationStatus = "VOIDED";
    }

    /// <summary>Returns the refund previously issued under this idempotency key, if any.</summary>
    public Refund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Validates that a refund of <paramref name="amount"/> stays within the captured total. Call
    /// before contacting PayPal so a partly-refunded order can never become over-refundable.
    /// </summary>
    public void GuardRefundable(decimal amount)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
            throw new InvalidPaymentStateException(
                $"Cannot refund a payment that is {Status}; only captured payments can be refunded.");

        if (amount <= 0m)
            throw new InvalidPaymentStateException("Refund amount must be positive.");

        if (amount > RemainingRefundable)
            throw new RefundExceedsCaptureException(amount, RemainingRefundable);
    }

    public Refund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        var refund = new Refund(payPalRefundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = TotalRefunded >= (CapturedGrossAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }
}
