using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// One attempt to pay an order by card. The row doubles as the duplicate-payment claim: its key is
/// derived from the order and the attempt number, so two concurrent attempts collide on insert.
/// </summary>
public class OrderPayment
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, int attemptNumber, string currency, long amountMinor, decimal amount, DateTimeOffset now)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.OutOfRange(attemptNumber, nameof(attemptNumber), 1, int.MaxValue);
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        Guard.Against.OutOfRange(amountMinor, nameof(amountMinor), 1, long.MaxValue);

        Id = BuildId(orderId, attemptNumber);
        OrderId = orderId;
        AttemptNumber = attemptNumber;
        IdempotencyKey = Guid.NewGuid().ToString();
        MerchantReference = $"eshop-{Id}-{IdempotencyKey[..8]}";
        Currency = currency;
        AmountMinor = amountMinor;
        Amount = amount;
        Status = PaymentAttemptStatus.Processing;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static string BuildId(int orderId, int attemptNumber) => $"{orderId}-P{attemptNumber}";

    public string Id { get; private set; }
    public int OrderId { get; private set; }
    public int AttemptNumber { get; private set; }

    /// <summary>Sent to the provider as its idempotency key; reused for every re-send of this attempt.</summary>
    public string IdempotencyKey { get; private set; }
    public string MerchantReference { get; private set; }
    public string Currency { get; private set; }
    public long AmountMinor { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }
    public string? PspReference { get; private set; }
    public string? ResultCode { get; private set; }
    public string? RefusalReason { get; private set; }
    public string? RefusalReasonCode { get; private set; }
    public string? ShopperMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The attempt may still turn into a charge, so no new attempt may start.</summary>
    public bool IsInFlight => Status is PaymentAttemptStatus.Processing or PaymentAttemptStatus.Pending or PaymentAttemptStatus.Unknown;

    public void MarkAuthorised(string? pspReference, string resultCode, DateTimeOffset now)
    {
        Status = PaymentAttemptStatus.Authorised;
        PspReference = pspReference;
        ResultCode = resultCode;
        RefusalReason = null;
        RefusalReasonCode = null;
        ShopperMessage = null;
        UpdatedAt = now;
    }

    public void MarkRefused(string? pspReference, string resultCode, string? refusalReason, string? refusalReasonCode,
        string shopperMessage, DateTimeOffset now)
    {
        Status = PaymentAttemptStatus.Refused;
        PspReference = pspReference;
        ResultCode = resultCode;
        RefusalReason = refusalReason;
        RefusalReasonCode = refusalReasonCode;
        ShopperMessage = shopperMessage;
        UpdatedAt = now;
    }

    public void MarkPending(string? pspReference, string resultCode, DateTimeOffset now)
    {
        Status = PaymentAttemptStatus.Pending;
        PspReference = pspReference;
        ResultCode = resultCode;
        ShopperMessage = "The payment is being confirmed by the card issuer.";
        UpdatedAt = now;
    }

    public void MarkFailed(string? pspReference, string? resultCode, string shopperMessage, DateTimeOffset now)
    {
        Status = PaymentAttemptStatus.Failed;
        PspReference = pspReference ?? PspReference;
        ResultCode = resultCode ?? ResultCode;
        ShopperMessage = shopperMessage;
        UpdatedAt = now;
    }

    public void MarkUnknown(string message, DateTimeOffset now)
    {
        Status = PaymentAttemptStatus.Unknown;
        ShopperMessage = message;
        UpdatedAt = now;
    }
}
