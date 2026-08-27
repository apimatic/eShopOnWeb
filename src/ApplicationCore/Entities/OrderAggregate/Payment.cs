using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// PayPal payment state for an order: the authorization (hold), the capture
/// (money taken at fulfilment) and any refunds. Carries the PayPal-owned ids and
/// statuses so a later request can act on the payment, plus the idempotency keys
/// used for each operation.
/// </summary>
public class Payment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, decimal amount, string currency, string authorizeIdempotencyKey)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(authorizeIdempotencyKey, nameof(authorizeIdempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        AuthorizedAmount = amount;
        Currency = currency;
        AuthorizeIdempotencyKey = authorizeIdempotencyKey;
    }

    public int OrderId { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string Currency { get; private set; }

    /// <summary>Key sent as PayPal-Request-Id for the authorize call.</summary>
    public string AuthorizeIdempotencyKey { get; private set; }

    /// <summary>Number of authorization attempts; keeps regenerated idempotency keys unique.</summary>
    public int AuthorizationAttempt { get; private set; } = 1;

    /// <summary>Invoice id sent to PayPal for the current authorization attempt.</summary>
    public string? InvoiceId { get; private set; }

    public void NextAuthorizationAttempt(string authorizeIdempotencyKey, string invoiceId)
    {
        AuthorizationAttempt++;
        AuthorizeIdempotencyKey = authorizeIdempotencyKey;
        InvoiceId = invoiceId;
    }

    public void SetInvoiceId(string invoiceId)
    {
        InvoiceId = invoiceId;
    }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>Vault token of the saved card used to pay, if any.</summary>
    public string? VaultTokenId { get; private set; }

    public string? CaptureIdempotencyKey { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void SetAuthorization(string payPalOrderId, string authorizationId, string status,
        DateTimeOffset? expiresAt, string? vaultTokenId)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        VaultTokenId = vaultTokenId;
    }

    public void SetAuthorizationStatus(string status, DateTimeOffset? expiresAt)
    {
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>
    /// Drops a dead authorization so the order can be paid again.
    /// </summary>
    public void ClearAuthorization()
    {
        PayPalOrderId = null;
        AuthorizationId = null;
        AuthorizationStatus = null;
        AuthorizationExpiresAt = null;
        VaultTokenId = null;
    }

    public void SetCapture(string captureId, string status, decimal amount, decimal? fee, decimal? net,
        string captureIdempotencyKey)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = net;
        CaptureIdempotencyKey = captureIdempotencyKey;
    }

    public void SetCaptureStatus(string status)
    {
        CaptureStatus = status;
    }

    public decimal TotalRefunded =>
        _refunds.Where(r => r.Status != PaymentRefundStatuses.Failed).Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, string? payPalRefundId, string status)
    {
        var refund = new PaymentRefund(Id, idempotencyKey, amount, Currency, payPalRefundId, status);
        _refunds.Add(refund);
        return refund;
    }
}

public static class PaymentRefundStatuses
{
    public const string Pending = "PENDING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
}
