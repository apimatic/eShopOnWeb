using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// One-off card details for a direct card payment or for vaulting. The full number is passed straight to
/// PayPal and never persisted or logged by this application. <see cref="Expiry"/> is <c>YYYY-MM</c>.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? Name,
    string? BillingAddressLine1,
    string? BillingAddressLine2,
    string? BillingCity,
    string? BillingState,
    string? BillingPostalCode,
    string? BillingCountryCode);

/// <summary>Result of authorizing (holding) an order's funds.</summary>
public record AuthorizeResult(string PayPalOrderId, string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

/// <summary>Result of capturing an authorized payment at fulfilment, with PayPal's reported fee/net.</summary>
public record CaptureResult(string CaptureId, string Status, decimal CapturedAmount, decimal? PayPalFee, decimal? NetAmount);

/// <summary>Result of renewing (re-authorizing) a stale authorization; carries the new authorization id.</summary>
public record ReauthorizeResult(string AuthorizationId, string Status, DateTimeOffset? ExpiresAt);

/// <summary>Result of refunding a captured payment.</summary>
public record RefundResult(string RefundId, string Status, decimal Amount);

/// <summary>Safe description of a vaulted card — never the full number.</summary>
public record VaultedCardResult(string VaultId, string? Brand, string? LastFourDigits, string? Expiry);

/// <summary>A PayPal-recorded transaction, as reported by transaction search, for reconciliation.</summary>
public record ReconciliationTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    DateTimeOffset? Date,
    string? InvoiceId);
