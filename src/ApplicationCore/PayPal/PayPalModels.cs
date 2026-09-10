using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>A monetary amount as PayPal models it: ISO-4217 code plus a string value
/// carried to the minor unit (e.g. "100.00").</summary>
public record PayPalMoney(string CurrencyCode, string Value)
{
    /// <summary>Builds a PayPal amount from a decimal, formatted to two decimal places
    /// (the minor unit for USD and most currencies) so the value is exact to the cent.</summary>
    public static PayPalMoney FromDecimal(decimal amount, string currencyCode) =>
        new(currencyCode, amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>Raw card details for a card-not-present payment or a vault request.
/// These are passed straight through to PayPal and are never persisted or logged by eShop.</summary>
public class CardDetails
{
    public required string Number { get; init; }
    /// <summary>Card expiry in PayPal's required "YYYY-MM" format.</summary>
    public required string ExpiryYearMonth { get; init; }
    public required string SecurityCode { get; init; }
    public string? Name { get; init; }

    // Optional billing address (maps to PayPal address_portable).
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }       // admin_area_2
    public string? State { get; init; }       // admin_area_1
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; } // 2-letter
}

/// <summary>Result of placing an authorization hold (via a fresh order or a reauthorization).</summary>
public record PayPalAuthorizationResult(
    string? PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorization, including PayPal's fee breakdown.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Gross,
    decimal? PayPalFee,
    decimal? Net,
    string CurrencyCode);

/// <summary>Result of a refund.</summary>
public record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount);

/// <summary>Safe, display-only details of a vaulted card. Never full card data.</summary>
public record VaultedCardResult(
    string VaultId,
    string? Brand,
    string? Last4,
    string? Expiry);

/// <summary>PayPal's own record of a transaction, from the reporting API.</summary>
public record PayPalTransaction(
    string TransactionId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset Date,
    string? EventCode);
