using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// One exchange with the payment provider, kept with the order for support staff: the provider's response
/// exactly as it arrived on the wire (including fields this build does not know about), or the transport
/// failure that left the outcome unknown. Card data is never stored here — only what the provider returned.
/// </summary>
public class PaymentProviderRecordEntry
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentProviderRecordEntry() { }

    internal PaymentProviderRecordEntry(int orderId, string provider, string operation, int? paymentAttemptNumber,
        Guid? refundId, string idempotencyKey, string merchantReference, int? httpStatus, string? responseBody,
        string? transportError, DateTimeOffset recordedAt)
    {
        Guard.Against.NullOrWhiteSpace(provider, nameof(provider));
        Guard.Against.NullOrWhiteSpace(operation, nameof(operation));

        Id = Guid.NewGuid();
        OrderId = orderId;
        Provider = provider;
        Operation = operation;
        PaymentAttemptNumber = paymentAttemptNumber;
        RefundId = refundId;
        IdempotencyKey = idempotencyKey;
        MerchantReference = merchantReference;
        HttpStatus = httpStatus;
        ResponseBody = responseBody;
        TransportError = transportError;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }
    public int OrderId { get; private set; }
    public string Provider { get; private set; }

    /// <summary>"payment" or "refund".</summary>
    public string Operation { get; private set; }

    public int? PaymentAttemptNumber { get; private set; }
    public Guid? RefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string MerchantReference { get; private set; }

    /// <summary>The HTTP status the provider answered with; null when no response arrived.</summary>
    public int? HttpStatus { get; private set; }

    /// <summary>The provider's response body, verbatim.</summary>
    public string? ResponseBody { get; private set; }

    /// <summary>Why no usable response arrived, when that is the case.</summary>
    public string? TransportError { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
}
