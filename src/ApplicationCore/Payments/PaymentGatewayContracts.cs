using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Card fields exactly as the payment provider's checkout front end hands them over (client-side encrypted).
/// The shop never sees a plain card number.
/// </summary>
public sealed record EncryptedCard(
    string EncryptedCardNumber,
    string EncryptedExpiryMonth,
    string EncryptedExpiryYear,
    string EncryptedSecurityCode,
    string HolderName)
{
    // Never print card data, not even encrypted, through a record's generated ToString.
    public override string ToString() => "EncryptedCard { *** }";
}

public enum RefundReason
{
    Fraud,
    CustomerRequest,
    Return,
    Duplicate,
    Other
}

public sealed record PaymentAuthorisationRequest(
    int OrderId,
    string Reference,
    string IdempotencyKey,
    long AmountInMinorUnits,
    string Currency,
    EncryptedCard Card);

public sealed record ProviderRefundRequest(
    int OrderId,
    string PaymentPspReference,
    string Reference,
    string IdempotencyKey,
    long AmountInMinorUnits,
    string Currency,
    RefundReason? Reason);

public enum ProviderCallOutcome
{
    /// <summary>Payment: the full amount was authorised.</summary>
    Authorised,

    /// <summary>Payment: the card or issuer declined.</summary>
    Refused,

    /// <summary>Payment: the provider holds the payment without a final result.</summary>
    Pending,

    /// <summary>Payment: the issuer requires shopper interaction this API cannot perform.</summary>
    ActionRequired,

    /// <summary>Payment: only part of the amount was authorised.</summary>
    PartiallyAuthorised,

    /// <summary>Refund: the provider accepted the refund request.</summary>
    Accepted,

    /// <summary>The provider rejected the request as invalid (the caller's data); nothing happened.</summary>
    Rejected,

    /// <summary>The provider refused us (credentials, permissions, configuration); nothing happened.</summary>
    ProviderUnavailable,

    /// <summary>No usable answer (timeout, connection failure, provider 5xx, unreadable success): the call may have taken effect.</summary>
    Unknown
}

/// <summary>One response (or the absence of one) from the provider, verbatim.</summary>
public sealed record CapturedProviderResponse(DateTimeOffset ReceivedAt, int? HttpStatus, string? Body, string? Note);

public sealed record PaymentAuthorisationResult
{
    public required ProviderCallOutcome Outcome { get; init; }
    public string? PspReference { get; init; }
    public string? ResultCode { get; init; }
    public string? RefusalReason { get; init; }
    public string? RefusalReasonCode { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int? HttpStatus { get; init; }

    /// <summary>True when no response arrived at all (timeout or connection failure).</summary>
    public bool NoResponse { get; init; }

    public long? AuthorisedAmountInMinorUnits { get; init; }
    public string? AuthorisedCurrency { get; init; }
    public IReadOnlyList<CapturedProviderResponse> Responses { get; init; } = Array.Empty<CapturedProviderResponse>();
}

public sealed record PaymentRefundResult
{
    public required ProviderCallOutcome Outcome { get; init; }
    public string? PspReference { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int? HttpStatus { get; init; }

    /// <summary>True when no response arrived at all (timeout or connection failure).</summary>
    public bool NoResponse { get; init; }

    public IReadOnlyList<CapturedProviderResponse> Responses { get; init; } = Array.Empty<CapturedProviderResponse>();
}
