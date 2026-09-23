using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>Raw card details for a one-off payment or to vault. Never persisted or logged by the app.</summary>
public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName = null,
    CardBillingAddress? BillingAddress = null);

public record CardBillingAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? AdminArea1,
    string? AdminArea2,
    string? PostalCode,
    string? CountryCode);

/// <summary>How to fund a payment: either a one-off card, or a previously vaulted card by its vault id.</summary>
public record PaymentSourceInput(CardDetails? Card, string? VaultId);

/// <summary>The information the gateway needs to create a PayPal order (intent = AUTHORIZE).</summary>
public record CreatePayPalOrderRequest(
    decimal Amount,
    string CurrencyCode,
    string InvoiceId,
    string CustomId,
    string? Description,
    PaymentSourceInput PaymentSource);

/// <summary>Outcome of creating or reading a PayPal order.</summary>
public record PayPalOrderResult(
    string Id,
    string? Status,
    bool RequiresBuyerAction,
    string? AuthorizationId,
    string? AuthorizationStatus,
    string? CaptureId = null,
    string? CaptureStatus = null);

/// <summary>Outcome of an authorization (hold) or re-authorization.</summary>
public record AuthorizationResult(
    string Id,
    string? Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? CreatedAt);

/// <summary>Outcome of capturing an authorized payment, with what PayPal reported for fee and net.</summary>
public record CaptureResult(
    string Id,
    string? Status,
    decimal? GrossAmount,
    decimal? PaypalFee,
    decimal? NetAmount,
    string? CurrencyCode,
    DateTimeOffset? CreatedAt);

/// <summary>Outcome of a refund.</summary>
public record RefundResult(
    string Id,
    string? Status,
    decimal? Amount,
    string? CurrencyCode);

/// <summary>Outcome of voiding an authorization.</summary>
public record VoidResult(string? Status);

/// <summary>The information the gateway needs to vault a card.</summary>
public record VaultCardRequest(CardDetails Card, string MerchantCustomerId);

/// <summary>Outcome of vaulting a card — the vault id plus a safe descriptor of the card.</summary>
public record VaultCardResult(
    string VaultId,
    string? Brand,
    string? LastDigits,
    string? Expiry,
    string? CardholderName);

/// <summary>A single transaction PayPal knows about, as reported by transaction search.</summary>
public record PayPalTransaction(
    string? TransactionId,
    string? InvoiceId,
    decimal? Amount,
    decimal? FeeAmount,
    string? CurrencyCode,
    string? Status,
    DateTimeOffset? InitiationDate);

/// <summary>One page of transaction search results.</summary>
public record TransactionSearchPage(
    IReadOnlyList<PayPalTransaction> Transactions,
    int Page,
    int TotalPages);
