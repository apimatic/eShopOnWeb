using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Card details exactly as the provider's checkout front end hands them over: every card field is encrypted
/// client-side, so the shop never sees a plain card number.
/// </summary>
public sealed class EncryptedCardDetails
{
    public EncryptedCardDetails(string encryptedCardNumber, string encryptedExpiryMonth, string encryptedExpiryYear,
        string encryptedSecurityCode, string holderName)
    {
        EncryptedCardNumber = encryptedCardNumber;
        EncryptedExpiryMonth = encryptedExpiryMonth;
        EncryptedExpiryYear = encryptedExpiryYear;
        EncryptedSecurityCode = encryptedSecurityCode;
        HolderName = holderName;
    }

    public string EncryptedCardNumber { get; }
    public string EncryptedExpiryMonth { get; }
    public string EncryptedExpiryYear { get; }
    public string EncryptedSecurityCode { get; }
    public string HolderName { get; }

    // Never let card data reach a log line through string formatting.
    public override string ToString() => "[card details redacted]";
}

public sealed record PaymentAuthorisationRequest(
    string Reference,
    string MerchantOrderReference,
    string IdempotencyKey,
    long AmountMinor,
    string Currency,
    EncryptedCardDetails Card,
    string ReturnUrl);

public sealed record GatewayRefundRequest(
    string Reference,
    string IdempotencyKey,
    string PaymentPspReference,
    long AmountMinor,
    string Currency);

/// <summary>A raw response received from the provider, kept verbatim.</summary>
public sealed record ProviderExchange(DateTimeOffset ReceivedAt, int HttpStatusCode, string Body);

public enum PaymentAuthorisationOutcome
{
    Authorised,
    /// <summary>The issuer or the provider declined the card; no money was taken.</summary>
    Refused,
    /// <summary>The card needs shopper authentication (e.g. 3-D Secure) this checkout cannot perform.</summary>
    ActionRequired,
    /// <summary>Accepted by the provider but not final yet.</summary>
    Pending,
    /// <summary>The provider rejected the request itself (invalid data); no money was taken.</summary>
    Rejected,
    /// <summary>The provider could not be used (configuration, credentials); no money was taken.</summary>
    ProviderError,
    /// <summary>No usable answer: the payment may or may not have been taken.</summary>
    Unknown
}

public enum RefundOutcome
{
    Received,
    Rejected,
    ProviderError,
    Unknown
}

public sealed record PaymentAuthorisationResult(
    PaymentAuthorisationOutcome Outcome,
    string? PspReference,
    string? ResultCode,
    string? RefusalReason,
    string? RefusalReasonCode,
    long? AuthorisedAmountMinor,
    string? ErrorCode,
    string? ErrorMessage,
    // True when the outcome is unknown because the provider did not answer in time.
    bool TimedOut,
    IReadOnlyList<ProviderExchange> Exchanges);

public sealed record RefundResult(
    RefundOutcome Outcome,
    string? PspReference,
    string? ErrorCode,
    string? ErrorMessage,
    bool TimedOut,
    IReadOnlyList<ProviderExchange> Exchanges);
