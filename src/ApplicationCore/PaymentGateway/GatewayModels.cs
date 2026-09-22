using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

/// <summary>
/// Provider-neutral models exchanged with <see cref="IPayPalGateway"/>. These deliberately carry no
/// PayPal SDK types so that ApplicationCore stays free of the SDK dependency.
/// </summary>
public sealed record GatewayMoney(string CurrencyCode, string Value);

/// <summary>Raw card details for a one-off (non-vaulted) payment. Never persisted, never logged.</summary>
public sealed record CardDetails(
    string Number,
    string Expiry,           // ISO-8601 YYYY-MM
    string? SecurityCode,
    string? Name,
    GatewayAddress? BillingAddress);

public sealed record GatewayAddress(
    string? AddressLine1,
    string? AdminArea2,      // city
    string? AdminArea1,      // state / province
    string? PostalCode,
    string? CountryCode);    // two-letter ISO-3166

public sealed record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    GatewayMoney? Amount);

public sealed record CaptureResult(
    string CaptureId,
    string Status,
    GatewayMoney? GrossAmount,
    GatewayMoney? PaypalFee,
    GatewayMoney? NetAmount);

public sealed record ReauthorizeResult(string AuthorizationId, string Status);

public sealed record RefundResult(string RefundId, string Status, GatewayMoney? Amount);

/// <summary>Safe (PCI-free) description of a vaulted card returned by the vault.</summary>
public sealed record VaultedCard(
    string VaultId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName);

public sealed record ReconciliationTransaction(
    string TransactionId,
    string? Status,
    string? CurrencyCode,
    string? Value,
    string? InitiationDate,
    string? InvoiceId,
    string? CustomField);

public sealed record ReconciliationResult(
    IReadOnlyList<ReconciliationTransaction> Transactions,
    int PagesScanned,
    int? TotalItems,
    bool Truncated);
