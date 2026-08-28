using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<OrderRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency));
        Currency = currency.ToUpperInvariant();
    }

    public OrderPaymentStatus Status { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public Guid OperationId { get; private set; } = Guid.NewGuid();
    public string Currency { get; private set; }
    public string? ProviderOrderId { get; private set; }
    public string? ProviderOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? AuthorizationStatusReason { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizationRenewedAt { get; private set; }
    public int? PaymentMethodId { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public string? CaptureStatusReason { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public Guid Version { get; private set; } = Guid.NewGuid();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();
    public decimal RefundedAmount => _refunds.Where(x => x.CountsAgainstCapture).Sum(x => x.Amount);

    public void RecordProviderOrder(string providerOrderId, string providerStatus)
    {
        ProviderOrderId = Required(providerOrderId, nameof(providerOrderId));
        ProviderOrderStatus = Required(providerStatus, nameof(providerStatus));
        Touch();
    }

    public void RecordAuthorization(string authorizationId, string providerStatus, string? statusReason,
        decimal amount, DateTimeOffset? createdAt, DateTimeOffset? expiresAt, int? paymentMethodId,
        bool renewed = false)
    {
        AuthorizationId = Required(authorizationId, nameof(authorizationId));
        AuthorizationStatus = Required(providerStatus, nameof(providerStatus));
        AuthorizationStatusReason = statusReason;
        AuthorizedAmount = amount;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentMethodId = paymentMethodId;
        if (renewed) AuthorizationRenewedAt = DateTimeOffset.UtcNow;
        Status = OrderPaymentStatus.Authorized;
        Touch();
    }

    public void RecordAuthorizationFailure(string providerStatus, string? statusReason)
    {
        AuthorizationStatus = providerStatus;
        AuthorizationStatusReason = statusReason;
        Status = OrderPaymentStatus.Failed;
        Touch();
    }

    public void RecordCapture(string captureId, string providerStatus, string? statusReason, decimal amount,
        decimal? paypalFee, decimal? netAmount, DateTimeOffset? capturedAt)
    {
        CaptureId = Required(captureId, nameof(captureId));
        CaptureStatus = Required(providerStatus, nameof(providerStatus));
        CaptureStatusReason = statusReason;
        CapturedAmount = amount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        Status = OrderPaymentStatus.Captured;
        Touch();
    }

    public void RecordVoid(string providerStatus)
    {
        AuthorizationStatus = Required(providerStatus, nameof(providerStatus));
        Status = OrderPaymentStatus.Cancelled;
        Touch();
    }

    public OrderRefund ReserveRefund(string idempotencyKey, decimal amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (_refunds.Any(x => x.IdempotencyKey == idempotencyKey))
            throw new InvalidOperationException("This refund idempotency key is already in use.");
        if (CapturedAmount is null || RefundedAmount + amount > CapturedAmount.Value)
            throw new InvalidOperationException("The refund exceeds the remaining captured amount.");

        var refund = new OrderRefund(idempotencyKey, amount, Currency);
        _refunds.Add(refund);
        Touch();
        return refund;
    }

    public void RefreshRefundState()
    {
        if (CapturedAmount is null) return;
        Status = RefundedAmount >= CapturedAmount.Value
            ? OrderPaymentStatus.Refunded
            : RefundedAmount > 0 ? OrderPaymentStatus.PartiallyRefunded : OrderPaymentStatus.Captured;
        Touch();
    }

    private void Touch() => Version = Guid.NewGuid();
    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value;
}
