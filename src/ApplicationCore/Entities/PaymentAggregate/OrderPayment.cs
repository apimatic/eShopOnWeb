using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency.ToUpperInvariant();
        Status = OrderPaymentStatus.AwaitingPayment;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public OrderPaymentStatus Status { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? CustomId { get; private set; }
    public string? InvoiceId { get; private set; }
    public string? PayIdempotencyKey { get; private set; }
    public int PayAttempt { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? AuthorizationCreated { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RemainingRefundable
    {
        get
        {
            var captured = CapturedAmount ?? 0m;
            var refunded = _refunds.Where(r => r.CountsAgainstCapturedAmount).Sum(r => r.Amount);
            return decimal.Round(captured - refunded, 2, MidpointRounding.AwayFromZero);
        }
    }

    public string BeginPayAttempt()
    {
        EnsureStatus(OrderPaymentStatus.AwaitingPayment, "This order is not awaiting payment.");
        PayAttempt++;
        PayIdempotencyKey = $"eshop-pay-{OrderId}-{PayAttempt}";
        return PayIdempotencyKey;
    }

    public void AttachPayPalOrder(string paypalOrderId, string customId, string invoiceId)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        PayPalOrderId = paypalOrderId;
        CustomId = customId;
        InvoiceId = invoiceId;
    }

    public void RecordAuthorization(string authorizationId, string status, DateTimeOffset? expiration, DateTimeOffset? created)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
        AuthorizationCreated = created;
        Status = OrderPaymentStatus.Authorized;
    }

    public void UpdateAuthorization(string authorizationId, string status, DateTimeOffset? expiration, DateTimeOffset? created)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
        if (created.HasValue)
        {
            AuthorizationCreated = created;
        }
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal? paypalFee, decimal? netProceeds)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = decimal.Round(capturedAmount, 2, MidpointRounding.AwayFromZero);
        PaypalFee = paypalFee.HasValue ? decimal.Round(paypalFee.Value, 2, MidpointRounding.AwayFromZero) : null;
        NetProceeds = netProceeds.HasValue ? decimal.Round(netProceeds.Value, 2, MidpointRounding.AwayFromZero) : null;
        AuthorizationStatus = "CAPTURED";
        Status = OrderPaymentStatus.Fulfilled;
    }

    public void RecordCancellation(string? authorizationStatus = "VOIDED")
    {
        if (Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new CheckoutException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (authorizationStatus is not null)
        {
            AuthorizationStatus = authorizationStatus;
        }

        Status = OrderPaymentStatus.Cancelled;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public PaymentRefund RecordRefund(string paypalRefundId, decimal amount, string status, string idempotencyKey)
    {
        EnsureCanRefund();
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (rounded <= 0)
        {
            throw new CheckoutException(400, "Refund amount must be greater than zero.");
        }

        if (rounded > RemainingRefundable)
        {
            throw new CheckoutException(400,
                $"Refund of {rounded} {Currency} exceeds the remaining refundable amount of {RemainingRefundable} {Currency}.");
        }

        var refund = new PaymentRefund(paypalRefundId, rounded, Currency, status, idempotencyKey);
        _refunds.Add(refund);
        RefreshRefundStatus();
        return refund;
    }

    public void RefreshRefundStatus()
    {
        if (Status is not OrderPaymentStatus.Fulfilled and not OrderPaymentStatus.Refunded and not OrderPaymentStatus.PartiallyRefunded)
        {
            return;
        }

        if (RemainingRefundable <= 0m)
        {
            Status = OrderPaymentStatus.Refunded;
            CaptureStatus = "REFUNDED";
        }
        else if (_refunds.Any(r => r.CountsAgainstCapturedAmount))
        {
            Status = OrderPaymentStatus.PartiallyRefunded;
            CaptureStatus = "PARTIALLY_REFUNDED";
        }
    }

    public void EnsureOwnedBy(string buyerId)
    {
        if (!string.Equals(BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new CheckoutException(404, $"Order {OrderId} was not found.");
        }
    }

    public void EnsureStatus(OrderPaymentStatus expected, string message)
    {
        if (Status != expected)
        {
            throw new CheckoutException(409, message);
        }
    }

    public void EnsureCanRefund()
    {
        if (Status is not OrderPaymentStatus.Fulfilled and not OrderPaymentStatus.PartiallyRefunded)
        {
            throw new CheckoutException(409, "Refunds are only available after the order has been fulfilled.");
        }

        if (string.IsNullOrEmpty(CaptureId))
        {
            throw new CheckoutException(409, "This order has no captured payment to refund.");
        }

        if (RemainingRefundable <= 0m)
        {
            throw new CheckoutException(409, "This payment has already been refunded in full.");
        }
    }
}
