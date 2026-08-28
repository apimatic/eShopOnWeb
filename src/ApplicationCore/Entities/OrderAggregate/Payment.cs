using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Payment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

    private Payment() { }

    internal Payment(string currency, decimal amount)
    {
        IntegrationId = Guid.NewGuid().ToString("N");
        InvoiceId = $"ESHOP-{IntegrationId}";
        Currency = currency;
        Amount = amount;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string IntegrationId { get; private set; } = null!;
    public string InvoiceId { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int ReauthorizationCount { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLastDigits { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public decimal RefundAmountCommitted => _refunds.Where(x => x.Status != "FAILED").Sum(x => x.Amount);

    public void MarkAttemptStarted()
    {
        PayPalOrderStatus = "CREATING";
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAuthorized(
        string payPalOrderId,
        string payPalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt,
        string? cardBrand,
        string? cardLastDigits)
    {
        PayPalOrderId = payPalOrderId;
        PayPalOrderStatus = payPalOrderStatus;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        AuthorizationCreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLastDigits = cardLastDigits;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string? payPalOrderId, string? payPalOrderStatus)
    {
        PayPalOrderId = payPalOrderId ?? PayPalOrderId;
        PayPalOrderStatus = payPalOrderStatus ?? "FAILED";
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkReauthorized(
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        AuthorizationCreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
        ReauthorizationCount++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCaptured(
        string captureId,
        string captureStatus,
        decimal amount,
        decimal? payPalFee,
        decimal? netAmount,
        DateTimeOffset? capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = amount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkVoided(string status)
    {
        AuthorizationStatus = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund AddRefund(string idempotencyKey, string payPalRequestId, decimal amount)
    {
        var refund = new PaymentRefund(idempotencyKey, payPalRequestId, amount);
        _refunds.Add(refund);
        UpdatedAt = DateTimeOffset.UtcNow;
        return refund;
    }
}
