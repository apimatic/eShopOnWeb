using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>
/// Raw card details supplied for a one-off payment or to vault a card. This type is transient:
/// it is never persisted to the application's own database and never written to logs — see
/// <see cref="ToString"/>, which deliberately redacts the PAN and security code.
/// </summary>
public sealed record CardDetails(
    string Number,
    string Expiry,          // YYYY-MM per the PayPal spec (date_year_month)
    string SecurityCode,
    string Name,
    BillingAddressDetails BillingAddress)
{
    public string Last4 => Number.Length >= 4 ? Number[^4..] : Number;

    // Records auto-generate a ToString that prints every member; override it so an accidental
    // log of a CardDetails can never leak the full card number or CVV.
    public override string ToString() => $"CardDetails {{ Name = {Name}, Last4 = ****{Last4}, Expiry = {Expiry} }}";

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Name = ").Append(Name).Append(", Last4 = ****").Append(Last4).Append(", Expiry = ").Append(Expiry);
        return true;
    }
}

/// <summary>The portable international billing address (PayPal <c>billing_address</c> shape).</summary>
public sealed record BillingAddressDetails(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea2,     // city
    string? AdminArea1,     // state / province
    string? PostalCode,
    string CountryCode);    // two-letter ISO-3166 country code (required by the spec)

/// <summary>A request to authorize (hold) an order total on a card or a saved card.</summary>
public sealed class AuthorizeCardRequest
{
    /// <summary>The eShop order id, sent as PayPal <c>invoice_id</c>/<c>custom_id</c> for reconciliation.</summary>
    public required string OrderReference { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }

    /// <summary>Card details for a one-off payment. Mutually exclusive with <see cref="VaultId"/>.</summary>
    public CardDetails? Card { get; init; }

    /// <summary>A saved card's PayPal vault id. Mutually exclusive with <see cref="Card"/>.</summary>
    public string? VaultId { get; init; }
}

/// <summary>The state PayPal owns for the hold on an order.</summary>
public sealed record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>What PayPal reported when the authorization was captured at fulfilment.</summary>
public sealed record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount);

/// <summary>The state PayPal owns for a refund.</summary>
public sealed record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>The safe descriptor of a vaulted card — never the full card details.</summary>
public sealed record SavedCardResult(
    string VaultId,
    string CustomerId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardHolderName);

/// <summary>One PayPal transaction as reported by the Transaction Search API, projected for reconciliation.</summary>
public sealed record PayPalTransactionRecord(
    string? TransactionId,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate,
    DateTimeOffset? UpdatedDate);
