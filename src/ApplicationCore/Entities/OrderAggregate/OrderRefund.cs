using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A refund of (part of) an order's payment. The (OrderId, Sequence) primary key is the claim that
/// serialises refunds of one order: each refund reserves its amount before the provider is called,
/// so concurrent refunds can never add up to more than was paid.
/// </summary>
public class OrderRefund
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(int orderId, int sequence, string paymentPspReference, long amountMinorUnits,
        string currency, string? reason, string requestedBy, DateTimeOffset createdAt)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NegativeOrZero(sequence, nameof(sequence));
        Guard.Against.NullOrWhiteSpace(paymentPspReference, nameof(paymentPspReference));
        Guard.Against.NegativeOrZero(amountMinorUnits, nameof(amountMinorUnits));
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        Guard.Against.NullOrWhiteSpace(requestedBy, nameof(requestedBy));

        OrderId = orderId;
        Sequence = sequence;
        RefundId = FormatRefundId(orderId, sequence);
        IdempotencyKey = Guid.NewGuid().ToString();
        MerchantReference = $"ESHOP-ORDER-{orderId}-REFUND-{sequence}";
        PaymentPspReference = paymentPspReference;
        AmountMinorUnits = amountMinorUnits;
        Currency = currency;
        Reason = reason;
        RequestedBy = requestedBy;
        Status = RefundStatus.Requested;
        CreatedAt = createdAt;
    }

    public static string FormatRefundId(int orderId, int sequence) => $"{orderId}-R{sequence}";

    public int OrderId { get; private set; }
    public int Sequence { get; private set; }
    public string RefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string MerchantReference { get; private set; }
    public string PaymentPspReference { get; private set; }
    public long AmountMinorUnits { get; private set; }
    public string Currency { get; private set; }
    public string? Reason { get; private set; }
    public string RequestedBy { get; private set; }
    public RefundStatus Status { get; private set; }
    public string? PspReference { get; private set; }
    public string? ProviderStatus { get; private set; }
    public string? ProviderErrorCode { get; private set; }
    public string? ProviderMessage { get; private set; }
    public int? ProviderHttpStatus { get; private set; }

    /// <summary>The provider's response body exactly as received, including fields this build does not model.</summary>
    public string? ProviderResponse { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>True while this refund's amount counts against what is still refundable.</summary>
    public bool ReservesAmount => Status != RefundStatus.Failed;

    public void Record(RefundStatus status, RefundProviderResult result, DateTimeOffset at)
    {
        Status = status;
        PspReference = result.PspReference ?? PspReference;
        ProviderStatus = result.ProviderStatus ?? ProviderStatus;
        ProviderErrorCode = result.ErrorCode;
        ProviderMessage = result.ErrorMessage;
        ProviderHttpStatus = result.HttpStatus ?? ProviderHttpStatus;
        ProviderResponse = result.RawResponse ?? ProviderResponse;
        CompletedAt = status is RefundStatus.Requested or RefundStatus.Unknown ? null : at;
    }

    public void Reject(string message, DateTimeOffset at)
    {
        Status = RefundStatus.Failed;
        ProviderMessage = message;
        CompletedAt = at;
    }
}
