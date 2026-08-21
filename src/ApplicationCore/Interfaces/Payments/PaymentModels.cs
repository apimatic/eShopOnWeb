using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>Billing address for a raw card, mapped to PayPal's address model by the processor.</summary>
public record CardBillingAddress(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

/// <summary>
/// Raw card details for a one-off payment or for vaulting. These never touch the application
/// database and are never logged — the processor forwards them straight to PayPal.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,          // "YYYY-MM"
    string? SecurityCode,
    string? Name,
    CardBillingAddress? BillingAddress);

/// <summary>
/// A request to place an authorization hold. Exactly one payment source is used: a raw
/// <see cref="Card"/> for a one-off payment, or a <see cref="VaultId"/> naming a saved card.
/// </summary>
public record PaymentAuthorizationRequest(
    string OrderReference,   // local order id, echoed to PayPal as invoice_id / custom_id for reconciliation
    decimal Amount,
    CardDetails? Card,
    string? VaultId);

/// <summary>Outcome of an authorization (or re-authorization): the hold PayPal now owns.</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Current state of a hold — used for staleness checks and re-authorization results.</summary>
public record AuthorizationSnapshot(
    string AuthorizationId,
    string? Status,
    DateTimeOffset? ExpiresAt);

/// <summary>Outcome of a capture, including what PayPal reported it kept and paid out.</summary>
public record CaptureResult(
    string CaptureId,
    string? Status,
    decimal GrossAmount,
    decimal? PayPalFee,
    decimal? NetAmount,
    string CurrencyCode);

/// <summary>Outcome of a refund.</summary>
public record RefundResult(
    string RefundId,
    string? Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>A vaulted card: the reusable token plus a safe descriptor for the shopper.</summary>
public record VaultedCard(
    string VaultId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? Name);

/// <summary>PayPal's own record of a transaction, as returned by transaction search.</summary>
public record PayPalTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? CurrencyCode,
    decimal? Fee,
    string? InvoiceId,
    string? CustomField,
    string? ReferenceId,
    string? ReferenceIdType,
    DateTimeOffset? InitiationDate);
