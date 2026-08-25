using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Tracks the PayPal-owned state of an Order's payment: the authorization (hold), the
/// capture (money actually taken) and any refunds. Child entity of the Order aggregate -
/// all mutation happens through Order or through the methods below, called by
/// OrderPaymentService while holding the parent Order.
/// </summary>
public class Payment : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    internal Payment(int orderId, decimal amount, string currencyCode, string payPalOrderId, string authorizeRequestId, int? paymentMethodId)
    {
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizeRequestId, nameof(authorizeRequestId));

        OrderId = orderId;
        Amount = amount;
        CurrencyCode = currencyCode;
        PayPalOrderId = payPalOrderId;
        AuthorizeRequestId = authorizeRequestId;
        PaymentMethodId = paymentMethodId;
        RefundedAmount = 0m;
    }

    public int OrderId { get; private set; }

    /// <summary>Order total that PayPal was asked to hold/capture, in <see cref="CurrencyCode"/>.</summary>
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>The saved card used to pay, if any (null for a one-off card payment).</summary>
    public int? PaymentMethodId { get; private set; }

    /// <summary>Id of the PayPal Order (v2 Orders API) created to authorize this payment.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>PayPal-Request-Id used for the initial authorize call, replayed on retry for idempotency.</summary>
    public string AuthorizeRequestId { get; private set; }

    public string? AuthorizationId { get; private set; }
    /// <summary>CREATED, CAPTURED, DENIED, EXPIRED, PARTIALLY_CAPTURED, VOIDED or PENDING.</summary>
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreateTime { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }

    /// <summary>Create time of the very first authorization, fixed even across reauthorizations - PayPal allows
    /// reauthorization only within 29 days of this moment.</summary>
    public DateTimeOffset? OriginalAuthorizationCreateTime { get; private set; }

    /// <summary>PayPal-Request-Id used for the capture call, replayed on retry for idempotency.</summary>
    public string? CaptureRequestId { get; private set; }
    public string? CaptureId { get; private set; }
    /// <summary>COMPLETED, DECLINED, PARTIALLY_REFUNDED, PENDING, REFUNDED or FAILED.</summary>
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CaptureTime { get; private set; }

    public decimal RefundedAmount { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    /// <summary>Amount still eligible to be refunded from the capture.</summary>
    public decimal RemainingRefundableAmount => (CapturedAmount ?? 0m) - RefundedAmount;

    internal void RecordAuthorization(string authorizationId, string status, DateTimeOffset createTime, DateTimeOffset? expirationTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationCreateTime = createTime;
        AuthorizationExpirationTime = expirationTime;
        OriginalAuthorizationCreateTime ??= createTime;
    }

    internal void RecordReauthorization(string newAuthorizationId, string status, DateTimeOffset createTime, DateTimeOffset? expirationTime)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        AuthorizationId = newAuthorizationId;
        AuthorizationStatus = status;
        AuthorizationCreateTime = createTime;
        AuthorizationExpirationTime = expirationTime;
    }

    internal void RecordVoid()
    {
        AuthorizationStatus = "VOIDED";
    }

    internal void RecordCapture(string captureId, string status, decimal capturedAmount, decimal feeAmount, decimal netAmount, string captureRequestId, DateTimeOffset captureTime)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFeeAmount = feeAmount;
        NetAmount = netAmount;
        CaptureRequestId = captureRequestId;
        CaptureTime = captureTime;
    }

    internal Refund AddRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey, DateTimeOffset createTime)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var refund = new Refund(Id, payPalRefundId, amount, CurrencyCode, status, idempotencyKey, createTime);
        _refunds.Add(refund);
        if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            RefundedAmount += amount;
        }
        return refund;
    }

    internal Refund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        foreach (var refund in _refunds)
        {
            if (string.Equals(refund.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
            {
                return refund;
            }
        }
        return null;
    }
}
