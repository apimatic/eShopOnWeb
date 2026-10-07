using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentAttemptStatus
{
    /// <summary>Claimed and sent (or about to be sent) to the payment provider.</summary>
    InFlight = 0,

    /// <summary>The provider authorised the payment and the money is being taken.</summary>
    Authorised = 1,

    /// <summary>The card was refused. No money was taken.</summary>
    Refused = 2,

    /// <summary>The provider rejected or failed the payment. No money was taken.</summary>
    Failed = 3,

    /// <summary>The card needs a shopper action (e.g. 3-D Secure) this API cannot perform. No money was taken.</summary>
    ActionRequired = 4,

    /// <summary>The provider accepted the payment but has not given a final outcome yet.</summary>
    Pending = 5,

    /// <summary>The call may or may not have reached the provider. Settled by re-sending with the same idempotency key.</summary>
    Unknown = 6
}

/// <summary>
/// One attempt to take the order total from a card. Keyed by (OrderId, AttemptNumber): the primary key is
/// what refuses a second concurrent claim for the same attempt.
/// </summary>
public class OrderPaymentAttempt
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPaymentAttempt() { }

    internal OrderPaymentAttempt(int orderId, int attemptNumber, long amountMinor, string currency, DateTimeOffset now)
    {
        Guard.Against.OutOfRange(attemptNumber, nameof(attemptNumber), 1, int.MaxValue);
        Guard.Against.NegativeOrZero(amountMinor, nameof(amountMinor));
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));

        OrderId = orderId;
        AttemptNumber = attemptNumber;
        IdempotencyKey = Guid.NewGuid().ToString();
        MerchantReference = $"eshop-order-{orderId}-payment-{attemptNumber}-{IdempotencyKey[..8]}";
        AmountMinor = amountMinor;
        Currency = currency;
        Status = PaymentAttemptStatus.InFlight;
        CreatedAt = now;
        LastSentAt = now;
    }

    public int OrderId { get; private set; }
    public int AttemptNumber { get; private set; }

    /// <summary>Sent to the provider so a re-send of this attempt can never charge twice.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>Our reference for the payment, as seen in the provider's back office.</summary>
    public string MerchantReference { get; private set; }

    /// <summary>The amount charged, in minor units of <see cref="Currency"/>.</summary>
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; }

    public PaymentAttemptStatus Status { get; private set; }
    public string? PspReference { get; private set; }
    public string? ResultCode { get; private set; }
    public string? RefusalReason { get; private set; }
    public string? RefusalReasonCode { get; private set; }

    /// <summary>What the shopper was told about this attempt.</summary>
    public string? ShopperMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSentAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>True while the attempt still blocks a new payment from starting.</summary>
    public bool IsOpen => Status is PaymentAttemptStatus.InFlight or PaymentAttemptStatus.Unknown or PaymentAttemptStatus.Pending;

    internal void MarkResent(DateTimeOffset now)
    {
        Status = PaymentAttemptStatus.InFlight;
        LastSentAt = now;
    }

    internal void RecordOutcome(PaymentAttemptStatus status, string? pspReference, string? resultCode,
        string? refusalReason, string? refusalReasonCode, string? shopperMessage, DateTimeOffset now)
    {
        Status = status;
        PspReference = pspReference ?? PspReference;
        ResultCode = resultCode ?? ResultCode;
        RefusalReason = refusalReason;
        RefusalReasonCode = refusalReasonCode;
        ShopperMessage = shopperMessage;
        CompletedAt = status is PaymentAttemptStatus.InFlight or PaymentAttemptStatus.Unknown ? null : now;
    }
}
