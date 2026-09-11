using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>Raw card details for a one-off payment or for vaulting. Never persisted or logged.</summary>
public record CardDetails(
    string Number,
    string Expiry,          // "YYYY-MM"
    string SecurityCode,
    string CardholderName,
    CardBillingAddress? BillingAddress);

public record CardBillingAddress(
    string? AddressLine1,
    string? AdminArea2,     // city
    string? AdminArea1,     // state / province
    string? PostalCode,
    string? CountryCode);

/// <summary>Result of authorizing an order total (placing a hold on the money).</summary>
public record PayPalAuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    string? CardBrand,
    string? CardLast4);

/// <summary>Result of capturing an authorization at fulfilment, including PayPal's own breakdown.</summary>
public record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string CurrencyCode);

public record PayPalReauthorizeResult(string AuthorizationId, string Status);

public record PayPalRefundResult(string RefundId, string Status, decimal? TotalRefunded);

/// <summary>Result of vaulting a card via the Payment Method Tokens API.</summary>
public record PayPalVaultCardResult(
    string VaultId,
    string CustomerId,
    string Brand,
    string Last4,
    string Expiry);

/// <summary>A single row from PayPal's Transaction Search (reporting) API.</summary>
public record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    decimal? FeeAmount,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    DateTimeOffset? Date);
