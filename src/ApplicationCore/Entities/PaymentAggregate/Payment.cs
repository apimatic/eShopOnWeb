using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class Payment : BaseEntity, IAggregateRoot
{
    private Payment() { }
    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        OrderId = orderId; BuyerId = buyerId; Amount = amount; Currency = currency; Status = PaymentStatus.AwaitingAuthorization;
    }
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public void Authorized(string orderId, string authorizationId, string status) { PayPalOrderId = orderId; AuthorizationId = authorizationId; AuthorizationStatus = status; Status = PaymentStatus.Authorized; AuthorizedAt = DateTimeOffset.UtcNow; }
    public void Captured(string captureId, string status, decimal amount, decimal? fee, decimal? net) { CaptureId = captureId; CaptureStatus = status; CapturedAmount = amount; PayPalFee = fee; NetAmount = net; Status = PaymentStatus.Captured; FulfilledAt = DateTimeOffset.UtcNow; }
    public void Cancelled(string status) { AuthorizationStatus = status; Status = PaymentStatus.Voided; }
    public void Refunded(decimal amount, string status) { Status = amount >= (CapturedAmount ?? Amount) ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded; CaptureStatus = status; }
}

public static class PaymentStatus
{
    public const string AwaitingAuthorization = "AwaitingAuthorization";
    public const string Authorized = "Authorized";
    public const string Captured = "Captured";
    public const string Voided = "Voided";
    public const string PartiallyRefunded = "PartiallyRefunded";
    public const string Refunded = "Refunded";
}
