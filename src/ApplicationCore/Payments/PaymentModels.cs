namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Card data exactly as the provider's checkout front end hands it over: every field is encrypted
/// client-side, so the shop never sees a plain card number. Never log or persist an instance.
/// </summary>
public sealed record EncryptedCardDetails(
    string EncryptedCardNumber,
    string EncryptedExpiryMonth,
    string EncryptedExpiryYear,
    string EncryptedSecurityCode,
    string HolderName)
{
    // Records print every member by default; keep card data out of logs and exception messages.
    public override string ToString() => "EncryptedCardDetails { *** }";
}

public sealed record CardPaymentRequest(
    string MerchantReference,
    string IdempotencyKey,
    long AmountMinorUnits,
    string Currency,
    EncryptedCardDetails Card);

public sealed record ProviderRefundRequest(
    string PaymentPspReference,
    string MerchantReference,
    string IdempotencyKey,
    long AmountMinorUnits,
    string Currency);

public enum PaymentProviderOutcome
{
    Authorised,
    // Declined by the issuer or the provider's risk checks.
    Refused,
    // The provider rejected the request as invalid (e.g. card data it could not decrypt). Nothing was charged.
    Rejected,
    // The payment needs a shopper interaction (redirect, 3-D Secure) this checkout does not perform.
    ActionRequired,
    // Accepted by the provider, final result not known yet.
    Pending,
    // The provider may have acted, but its answer could not be obtained or read.
    Unknown,
    // Our credentials/configuration were refused. Nothing was charged; not the shopper's fault.
    ProviderUnavailable
}

public sealed record PaymentProviderResult
{
    public required PaymentProviderOutcome Outcome { get; init; }
    public string? PspReference { get; init; }
    public string? ResultCode { get; init; }
    public string? RefusalReason { get; init; }
    public string? RefusalReasonCode { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int? HttpStatus { get; init; }

    /// <summary>The provider's response body verbatim, when one was received.</summary>
    public string? RawResponse { get; init; }
}

public enum RefundProviderOutcome
{
    // The provider accepted the refund request; it settles asynchronously.
    Received,
    Rejected,
    Unknown,
    ProviderUnavailable
}

public sealed record RefundProviderResult
{
    public required RefundProviderOutcome Outcome { get; init; }
    public string? PspReference { get; init; }
    public string? ProviderStatus { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int? HttpStatus { get; init; }

    /// <summary>The provider's response body verbatim, when one was received.</summary>
    public string? RawResponse { get; init; }
}
