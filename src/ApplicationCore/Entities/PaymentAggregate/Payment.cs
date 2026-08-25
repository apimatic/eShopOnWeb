using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class Payment : BaseEntity, IAggregateRoot
{
    private readonly List<OrderRefund> _refunds = new();

#pragma warning disable CS8618
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int eShopOrderId, string payPalOrderId, string createIdempotencyKey,
        string authorizeIdempotencyKey, DateTime createdAt)
    {
        EShopOrderId = eShopOrderId;
        PayPalOrderId = payPalOrderId;
        CreateIdempotencyKey = createIdempotencyKey;
        AuthorizeIdempotencyKey = authorizeIdempotencyKey;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public int EShopOrderId { get; private set; }
    public string PayPalOrderId { get; private set; } = string.Empty;
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? AuthorizationExpiryTime { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public string? CapturedAmountValue { get; private set; }
    public string? CapturedAmountCurrency { get; private set; }
    public string? PayPalFeeValue { get; private set; }
    public string? PayPalFeeCurrency { get; private set; }
    public string? NetAmountValue { get; private set; }
    public string? NetAmountCurrency { get; private set; }
    public DateTime? VoidedAt { get; private set; }
    public string CreateIdempotencyKey { get; private set; } = string.Empty;
    public string AuthorizeIdempotencyKey { get; private set; } = string.Empty;
    public string? CaptureIdempotencyKey { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public void SetAuthorization(string authorizationId, string? status, string? expiryTime)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiryTime = expiryTime;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetCapture(string captureId, string? status, string captureIdempotencyKey,
        string? grossValue, string? grossCurrency,
        string? feeValue, string? feeCurrency,
        string? netValue, string? netCurrency)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CaptureIdempotencyKey = captureIdempotencyKey;
        CapturedAmountValue = grossValue;
        CapturedAmountCurrency = grossCurrency;
        PayPalFeeValue = feeValue;
        PayPalFeeCurrency = feeCurrency;
        NetAmountValue = netValue;
        NetAmountCurrency = netCurrency;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetVoided()
    {
        VoidedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public decimal TotalRefundedAmount()
    {
        var total = 0m;
        foreach (var r in _refunds)
        {
            if (decimal.TryParse(r.AmountValue, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var amount))
                total += amount;
        }
        return total;
    }
}
