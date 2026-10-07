using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// One attempt to take the order total from the shopper's card.
/// </summary>
public class PaymentAttempt : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentAttempt() {}

    internal PaymentAttempt(string reference, string idempotencyKey, long amountMinor, string currency, DateTimeOffset createdAt)
    {
        Guard.Against.NullOrEmpty(reference, nameof(reference));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amountMinor, nameof(amountMinor));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        Reference = reference;
        IdempotencyKey = idempotencyKey;
        AmountMinor = amountMinor;
        Currency = currency;
        CreatedAt = createdAt;
        Status = PaymentAttemptStatus.Initiated;
    }

    public string Reference { get; private set; }
    public string IdempotencyKey { get; private set; }
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; }
    public PaymentAttemptStatus Status { get; private set; }
    public string? PspReference { get; private set; }
    public string? ResultCode { get; private set; }
    public string? RefusalReason { get; private set; }
    public string? RefusalReasonCode { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    /// <summary>The amount the provider reports as authorised; set once the attempt is authorised.</summary>
    public long? AuthorisedAmountMinor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private readonly List<ProviderResponseRecord> _providerResponses = new();
    public IReadOnlyCollection<ProviderResponseRecord> ProviderResponses => _providerResponses.AsReadOnly();

    /// <summary>True while the provider may still be holding a result for this attempt.</summary>
    public bool IsUnsettled => Status is PaymentAttemptStatus.Initiated or PaymentAttemptStatus.Pending or PaymentAttemptStatus.Unknown;

    internal void RecordOutcome(PaymentAttemptStatus status, string? pspReference, string? resultCode,
        string? refusalReason, string? refusalReasonCode, string? errorCode, string? errorMessage,
        long? authorisedAmountMinor, DateTimeOffset now)
    {
        Status = status;
        // A settle call can come back without data an earlier call already recorded; never erase it.
        PspReference = pspReference ?? PspReference;
        ResultCode = resultCode ?? ResultCode;
        RefusalReason = refusalReason;
        RefusalReasonCode = refusalReasonCode;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        AuthorisedAmountMinor = status == PaymentAttemptStatus.Authorised ? authorisedAmountMinor ?? AmountMinor : null;
        CompletedAt = IsUnsettled ? null : now;
    }

    internal void AddProviderResponse(ProviderResponseRecord response) => _providerResponses.Add(response);
}
