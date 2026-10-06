using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// One attempt to charge the order total. The (OrderId, AttemptNumber) primary key doubles as the
/// claim that stops two concurrent requests from charging the same order: the store refuses a second
/// row with the same key. The raw provider response is kept verbatim for support.
/// </summary>
public class OrderPaymentAttempt
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPaymentAttempt() { }

    public OrderPaymentAttempt(int orderId, int attemptNumber, long amountMinorUnits, string currency, DateTimeOffset createdAt)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NegativeOrZero(attemptNumber, nameof(attemptNumber));
        Guard.Against.NegativeOrZero(amountMinorUnits, nameof(amountMinorUnits));
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));

        OrderId = orderId;
        AttemptNumber = attemptNumber;
        AmountMinorUnits = amountMinorUnits;
        Currency = currency;
        IdempotencyKey = Guid.NewGuid().ToString();
        MerchantReference = $"ESHOP-ORDER-{orderId}-PAY-{attemptNumber}";
        Status = PaymentAttemptStatus.InFlight;
        CreatedAt = createdAt;
    }

    public int OrderId { get; private set; }
    public int AttemptNumber { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string MerchantReference { get; private set; }
    public long AmountMinorUnits { get; private set; }
    public string Currency { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }
    public string? PspReference { get; private set; }
    public string? ResultCode { get; private set; }
    public string? RefusalReason { get; private set; }
    public string? RefusalReasonCode { get; private set; }
    public string? ProviderErrorCode { get; private set; }
    public string? ProviderMessage { get; private set; }
    public int? ProviderHttpStatus { get; private set; }

    /// <summary>The provider's response body exactly as received, including fields this build does not model.</summary>
    public string? ProviderResponse { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>True while this attempt is, or may still turn into, a charge — so no other attempt may start.</summary>
    public bool BlocksNewAttempts =>
        Status is PaymentAttemptStatus.InFlight or PaymentAttemptStatus.Authorised
            or PaymentAttemptStatus.Unknown or PaymentAttemptStatus.Pending;

    public void Record(PaymentAttemptStatus status, PaymentProviderResult result, DateTimeOffset at)
    {
        Status = status;
        PspReference = result.PspReference ?? PspReference;
        ResultCode = result.ResultCode ?? ResultCode;
        RefusalReason = result.RefusalReason;
        RefusalReasonCode = result.RefusalReasonCode;
        ProviderErrorCode = result.ErrorCode;
        ProviderMessage = result.ErrorMessage;
        ProviderHttpStatus = result.HttpStatus ?? ProviderHttpStatus;
        // Keep the last body the provider actually sent; a failed read must not erase an earlier one.
        ProviderResponse = result.RawResponse ?? ProviderResponse;
        CompletedAt = status is PaymentAttemptStatus.InFlight or PaymentAttemptStatus.Unknown or PaymentAttemptStatus.Pending
            ? null
            : at;
    }

    public void Withdraw(DateTimeOffset at)
    {
        Status = PaymentAttemptStatus.Withdrawn;
        CompletedAt = at;
    }
}
