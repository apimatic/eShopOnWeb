using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details for a one-off payment or to vault. Never persisted in this app's database and
/// never logged. Passed to the gateway and discarded.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry,          // YYYY-MM
    string SecurityCode,
    string? Name = null,
    string? BillingAddressLine1 = null,
    string? BillingAdminArea1 = null,   // state / province
    string? BillingAdminArea2 = null,   // city
    string? BillingPostalCode = null,
    string? BillingCountryCode = null); // ISO-3166-1 alpha-2

/// <summary>Instruction for authorizing an order: pay with a one-off card, or a saved card's vault id.</summary>
public record PaymentAuthorizationRequest(
    Guid PaymentReference,
    decimal Amount,
    CardDetails? Card,
    string? VaultId,
    string? Description);

public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt,
    decimal Amount,
    string? PaymentMethodDescription);

/// <summary>Current provider-side authorization state, used to settle unknown outcomes / staleness.</summary>
public record AuthorizationLookup(string Status, DateTimeOffset? ExpiresAt);

public record CaptureResult(
    string CaptureId,
    string Status,
    decimal CapturedAmount,
    decimal? PayPalFee,
    decimal? NetAmount);

public record VoidResult(string Status);

public record RefundResult(string RefundId, string Status, decimal Amount);

public record VaultCardRequest(CardDetails Card, string MerchantCustomerId);

public record VaultResult(string VaultId, string? Brand, string? Last4, string? Expiry);

public record ReconciliationTransaction(
    string? TransactionId,
    string? Status,
    decimal? Amount,
    string? Currency,
    string? InvoiceId,
    string? CustomField);

/// <summary>
/// PayPal's own record of transactions for a date range. <see cref="Truncated"/> tells the caller
/// the walk hit a page cap and the set is partial — not just a log line.
/// </summary>
public record ReconciliationTransactions(
    IReadOnlyList<ReconciliationTransaction> Transactions,
    bool Truncated,
    int PagesFetched,
    int? TotalPages);
