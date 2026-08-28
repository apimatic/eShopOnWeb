using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<PaymentAuthorization> _authorizations = new();
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(string currency, decimal orderAmount)
    {
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        Guard.Against.NegativeOrZero(orderAmount, nameof(orderAmount));

        Currency = currency.ToUpperInvariant();
        OrderAmount = orderAmount;
        InvoiceId = $"ESHOP-{Guid.NewGuid():N}";
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; }
    public decimal OrderAmount { get; private set; }
    public string InvoiceId { get; private set; }
    public OrderPaymentStatus Status { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public int AuthorizationAttemptCount { get; private set; }
    public string? FundingBrand { get; private set; }
    public string? FundingLastDigits { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public IReadOnlyCollection<PaymentAuthorization> Authorizations => _authorizations.AsReadOnly();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public PaymentAuthorization? CurrentAuthorization => _authorizations.SingleOrDefault(x => x.IsCurrent);
    public decimal RefundedAmount => _refunds
        .Where(x => !string.Equals(x.Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        .Sum(x => x.Amount);

    public void RecordPayPalOrder(string payPalOrderId)
    {
        if (Status != OrderPaymentStatus.AwaitingPayment || PayPalOrderId is not null)
        {
            throw new InvalidOperationException("A PayPal order has already been assigned to this payment.");
        }
        PayPalOrderId = Guard.Against.NullOrWhiteSpace(payPalOrderId, nameof(payPalOrderId));
    }

    public int StartAuthorizationAttempt()
    {
        if (Status != OrderPaymentStatus.AwaitingPayment || PayPalOrderId is null)
        {
            throw new InvalidOperationException("A PayPal order must exist before authorization.");
        }
        return ++AuthorizationAttemptCount;
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        decimal amount,
        DateTimeOffset createdAt,
        DateTimeOffset expirationTime,
        string? fundingBrand,
        string? fundingLastDigits,
        int? savedPaymentMethodId)
    {
        if (Status != OrderPaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException("This order is not awaiting payment.");
        }

        if (!string.Equals(PayPalOrderId, payPalOrderId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The authorization belongs to an unexpected PayPal order.");
        }
        FundingBrand = fundingBrand;
        FundingLastDigits = fundingLastDigits;
        SavedPaymentMethodId = savedPaymentMethodId;
        _authorizations.Add(new PaymentAuthorization(
            authorizationId,
            authorizationStatus,
            amount,
            Currency,
            createdAt,
            expirationTime));
        Status = OrderPaymentStatus.Authorized;
    }

    public void RecordReauthorization(
        string authorizationId,
        string authorizationStatus,
        decimal amount,
        DateTimeOffset createdAt,
        DateTimeOffset expirationTime)
    {
        if (Status != OrderPaymentStatus.Authorized || CurrentAuthorization is null)
        {
            throw new InvalidOperationException("Only an authorized payment can be reauthorized.");
        }

        CurrentAuthorization.Supersede();
        _authorizations.Add(new PaymentAuthorization(
            authorizationId,
            authorizationStatus,
            amount,
            Currency,
            createdAt,
            expirationTime));
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? payPalFee,
        decimal? netAmount,
        DateTimeOffset capturedAt)
    {
        if (Status != OrderPaymentStatus.Authorized)
        {
            throw new InvalidOperationException("Only an authorized payment can be captured.");
        }

        CaptureId = Guard.Against.NullOrWhiteSpace(captureId, nameof(captureId));
        CaptureStatus = Guard.Against.NullOrWhiteSpace(captureStatus, nameof(captureStatus));
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        CurrentAuthorization?.MarkCaptured();
        Status = CaptureState(captureStatus);
    }

    public void UpdateCapture(
        string captureStatus,
        decimal capturedAmount,
        decimal? payPalFee,
        decimal? netAmount,
        DateTimeOffset capturedAt)
    {
        if (Status != OrderPaymentStatus.CapturePending || CaptureId is null)
        {
            throw new InvalidOperationException("This payment does not have a pending capture.");
        }

        CaptureStatus = Guard.Against.NullOrWhiteSpace(captureStatus, nameof(captureStatus));
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        if (string.Equals(captureStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            CurrentAuthorization?.MarkCaptured();
            Status = OrderPaymentStatus.Captured;
        }
        else if (!string.Equals(captureStatus, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            Status = OrderPaymentStatus.CaptureFailed;
        }
    }

    public void RecordVoid(string authorizationStatus)
    {
        if (Status != OrderPaymentStatus.Authorized)
        {
            throw new InvalidOperationException("Only an authorized payment can be voided.");
        }

        CurrentAuthorization?.UpdateStatus(authorizationStatus);
        Status = OrderPaymentStatus.Voided;
    }

    public PaymentRefund RecordRefund(
        string refundId,
        string refundStatus,
        decimal amount,
        string idempotencyKey,
        DateTimeOffset createdAt)
    {
        if (Status is not (OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new InvalidOperationException("Only a captured payment can be refunded.");
        }

        var refund = new PaymentRefund(refundId, refundStatus, amount, Currency, idempotencyKey, createdAt);
        _refunds.Add(refund);
        Status = RefundedAmount == CapturedAmount
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
        return refund;
    }

    private static OrderPaymentStatus CaptureState(string captureStatus) =>
        string.Equals(captureStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            ? OrderPaymentStatus.Captured
            : string.Equals(captureStatus, "PENDING", StringComparison.OrdinalIgnoreCase)
                ? OrderPaymentStatus.CapturePending
                : OrderPaymentStatus.CaptureFailed;
}
