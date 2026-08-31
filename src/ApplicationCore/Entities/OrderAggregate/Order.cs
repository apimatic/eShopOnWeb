using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Order() { }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.UtcNow;
    public Guid PaymentReference { get; private set; } = Guid.NewGuid();
    public Address ShipToAddress { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public FulfilmentStatus FulfilmentStatus { get; private set; } = FulfilmentStatus.Pending;
    public string? PaymentCurrency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public string? AuthorizationRequestId { get; private set; }
    public int AuthorizationAttempt { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? ReauthorizationRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public string? VoidRequestId { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    private readonly List<OrderItem> _orderItems = new();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total() => _orderItems.Sum(item => item.UnitPrice * item.Units);

    public string BeginAuthorization(string currency)
    {
        if (FulfilmentStatus != FulfilmentStatus.Pending ||
            PaymentStatus is not (PaymentStatus.AwaitingPayment or PaymentStatus.Authorizing or PaymentStatus.PayerActionRequired))
        {
            throw new InvalidOperationException("Only an order awaiting payment can be authorized.");
        }

        PaymentCurrency = currency;
        if (PaymentStatus == PaymentStatus.PayerActionRequired)
        {
            AuthorizationRequestId = null;
        }
        if (AuthorizationRequestId == null)
        {
            AuthorizationAttempt++;
            AuthorizationRequestId = OperationRequestId($"authorize:{AuthorizationAttempt}");
        }
        PaymentStatus = PaymentStatus.Authorizing;
        return AuthorizationRequestId;
    }

    public void CompleteAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        decimal amount,
        string currency,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        if (amount != Total())
        {
            throw new InvalidOperationException("PayPal authorized an amount that does not equal the order total.");
        }

        if (!string.Equals(currency, PaymentCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PayPal authorized an unexpected currency.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = PaymentStatus.Authorized;
    }

    public void FailAuthorization(bool payerActionRequired)
    {
        PaymentStatus = payerActionRequired ? PaymentStatus.PayerActionRequired : PaymentStatus.AwaitingPayment;
        if (!payerActionRequired)
        {
            AuthorizationRequestId = null;
        }
    }

    public string BeginReauthorization()
    {
        EnsureAuthorized();
        ReauthorizationRequestId ??= OperationRequestId($"reauthorize:{PayPalAuthorizationId}");
        return ReauthorizationRequestId;
    }

    public void CompleteReauthorization(
        string authorizationId,
        string authorizationStatus,
        decimal amount,
        string currency,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        EnsureAuthorized();
        if (amount != Total() || !string.Equals(currency, PaymentCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PayPal renewed the authorization for an unexpected amount or currency.");
        }
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        ReauthorizationRequestId = null;
    }

    public string BeginCapture()
    {
        if (PaymentStatus is not (PaymentStatus.Authorized or PaymentStatus.Capturing))
        {
            throw new InvalidOperationException("Only an authorized order can be fulfilled.");
        }

        CaptureRequestId ??= OperationRequestId("capture");
        PaymentStatus = PaymentStatus.Capturing;
        return CaptureRequestId;
    }

    public void CompleteCapture(
        string captureId,
        string captureStatus,
        decimal amount,
        decimal fee,
        decimal netProceeds,
        string currency,
        DateTimeOffset capturedAt)
    {
        if (amount != Total() || !string.Equals(currency, PaymentCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PayPal captured an amount or currency that does not equal the order total.");
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = netProceeds;
        CapturedAt = capturedAt;
        PayPalAuthorizationStatus = "CAPTURED";
        PaymentStatus = PaymentStatus.Captured;
        FulfilmentStatus = FulfilmentStatus.Fulfilled;
    }

    public void RecordPendingCapture(string captureId, string captureStatus)
    {
        if (PaymentStatus != PaymentStatus.Capturing)
        {
            throw new InvalidOperationException("The order is not awaiting capture completion.");
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
    }

    public string? BeginCancellation()
    {
        if (FulfilmentStatus != FulfilmentStatus.Pending ||
            PaymentStatus is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new InvalidOperationException("A fulfilled order cannot be cancelled; refund its capture instead.");
        }

        if (PaymentStatus == PaymentStatus.AwaitingPayment)
        {
            FulfilmentStatus = FulfilmentStatus.Cancelled;
            CancelledAt = DateTimeOffset.UtcNow;
            return null;
        }

        if (PaymentStatus is not (PaymentStatus.Authorized or PaymentStatus.Voiding))
        {
            throw new InvalidOperationException("This order is not in a cancellable payment state.");
        }

        VoidRequestId ??= OperationRequestId("void");
        PaymentStatus = PaymentStatus.Voiding;
        return VoidRequestId;
    }

    public void CompleteCancellation(string authorizationStatus)
    {
        PayPalAuthorizationStatus = authorizationStatus;
        PaymentStatus = PaymentStatus.Voided;
        FulfilmentStatus = FulfilmentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public OrderRefund BeginRefund(string idempotencyKey, decimal amount)
    {
        if (FulfilmentStatus != FulfilmentStatus.Fulfilled ||
            PaymentStatus is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidOperationException("Only a fulfilled order with refundable captured funds can be refunded.");
        }

        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            if (existing.Amount != amount)
            {
                throw new InvalidOperationException("This idempotency key was already used with a different refund amount.");
            }

            return existing;
        }

        var reserved = _refunds
            .Where(x => x.Status is RefundStatus.Pending or RefundStatus.Completed)
            .Sum(x => x.Amount);
        var refundable = CapturedAmount.GetValueOrDefault() - reserved;
        if (amount <= 0 || amount > refundable)
        {
            throw new InvalidOperationException($"Refund amount must be positive and no greater than the remaining refundable amount ({refundable:0.00}).");
        }

        var refund = new OrderRefund(idempotencyKey, amount, PaymentCurrency!);
        _refunds.Add(refund);
        return refund;
    }

    public void CompleteRefund(OrderRefund refund, string payPalRefundId, string status, decimal amount, string currency, DateTimeOffset completedAt)
    {
        if (!_refunds.Contains(refund) || refund.Amount != amount ||
            !string.Equals(currency, PaymentCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PayPal returned refund details that do not match the requested refund.");
        }

        refund.Complete(payPalRefundId, status, completedAt);
        var refunded = _refunds.Where(x => x.Status == RefundStatus.Completed).Sum(x => x.Amount);
        PaymentStatus = refunded == CapturedAmount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }

    public void RecordPendingRefund(OrderRefund refund, string payPalRefundId, string status)
    {
        if (!_refunds.Contains(refund))
        {
            throw new InvalidOperationException("The refund does not belong to this order.");
        }

        refund.RecordPending(payPalRefundId, status);
    }

    public void FailRefund(OrderRefund refund, string? failureReason)
    {
        if (!_refunds.Contains(refund))
        {
            throw new InvalidOperationException("The refund does not belong to this order.");
        }

        refund.Fail(failureReason);
    }

    private void EnsureAuthorized()
    {
        if (PaymentStatus != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(PayPalAuthorizationId))
        {
            throw new InvalidOperationException("The order has no active authorization.");
        }
    }

    private string OperationRequestId(string operation)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{PaymentReference:N}:{operation}"));
        return new Guid(bytes.AsSpan(0, 16)).ToString();
    }
}

public enum PaymentStatus
{
    AwaitingPayment,
    Authorizing,
    PayerActionRequired,
    Authorized,
    Capturing,
    Captured,
    Voiding,
    Voided,
    PartiallyRefunded,
    Refunded
}

public enum FulfilmentStatus
{
    Pending,
    Fulfilled,
    Cancelled
}
