using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Money given back to the shopper on a paid order, in full or in part.
/// </summary>
public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() {}

    internal OrderRefund(string reference, string idempotencyKey, string paymentPspReference, long amountMinor,
        string currency, string requestedBy, DateTimeOffset createdAt)
    {
        Guard.Against.NullOrEmpty(reference, nameof(reference));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(paymentPspReference, nameof(paymentPspReference));
        Guard.Against.NegativeOrZero(amountMinor, nameof(amountMinor));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(requestedBy, nameof(requestedBy));

        Reference = reference;
        IdempotencyKey = idempotencyKey;
        PaymentPspReference = paymentPspReference;
        AmountMinor = amountMinor;
        Currency = currency;
        RequestedBy = requestedBy;
        CreatedAt = createdAt;
        Status = RefundStatus.Initiated;
    }

    public string Reference { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string PaymentPspReference { get; private set; }
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; }
    public string RequestedBy { get; private set; }
    public RefundStatus Status { get; private set; }
    public string? PspReference { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private readonly List<ProviderResponseRecord> _providerResponses = new();
    public IReadOnlyCollection<ProviderResponseRecord> ProviderResponses => _providerResponses.AsReadOnly();

    public bool IsUnsettled => Status is RefundStatus.Initiated or RefundStatus.Unknown;

    /// <summary>Refunds that may have moved money count against what is still refundable.</summary>
    public bool CountsAgainstPayment => Status != RefundStatus.Failed;

    internal void RecordOutcome(RefundStatus status, string? pspReference, string? errorCode, string? errorMessage, DateTimeOffset now)
    {
        Status = status;
        PspReference = pspReference ?? PspReference;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        CompletedAt = IsUnsettled ? null : now;
    }

    internal void AddProviderResponse(ProviderResponseRecord response) => _providerResponses.Add(response);
}
