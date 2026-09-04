using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The PayPal-backed payment attached to an order. Carries enough of the state PayPal
/// owns (ids and current status for the hold, the capture and the refunds) that a later
/// request can act on it rather than only the request that started it.
/// </summary>
public class OrderPayment : BaseEntity
{
    public int OrderId { get; private set; }
    public Order? Order { get; private set; }

    public string PayPalOrderId { get; private set; } = string.Empty;
    public string AuthorizationId { get; private set; } = string.Empty;
    public string AuthorizationStatus { get; private set; } = string.Empty;
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }

    public decimal Amount { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Id of the saved card used for this payment, when one was used.</summary>
    public int? SavedCardId { get; private set; }

    /// <summary>A safe, human-recognisable description of the payment source (never full card details).</summary>
    public string PaymentSourceDescription { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpirationTime, decimal amount, string currency,
        string paymentSourceDescription, int? savedCardId)
    {
        OrderId = orderId;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = authorizationExpirationTime;
        Amount = amount;
        Currency = currency;
        PaymentSourceDescription = paymentSourceDescription;
        SavedCardId = savedCardId;
    }

    public void SetAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpirationTime)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = authorizationExpirationTime;
    }

    /// <summary>Renews a stale authorization with the fresh authorization PayPal returned.</summary>
    public void RenewAuthorization(string newAuthorizationId, string authorizationStatus, DateTimeOffset? authorizationExpirationTime)
    {
        AuthorizationId = newAuthorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = authorizationExpirationTime;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
    }

    public void MarkAuthorizationVoided(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
    }

    public void AddRefund(PaymentRefund refund)
    {
        _refunds.Add(refund);
    }

    public decimal TotalRefundedAmount => _refunds.Sum(r => r.Amount);
}