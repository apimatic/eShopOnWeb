using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Raw card details for a one-off payment or for vaulting. These flow straight through to PayPal and
/// are NEVER persisted in this application's database nor written to logs.
/// </summary>
public record CardDetails(
    string Number,
    string Expiry, // ISO-8601 YYYY-MM
    string SecurityCode,
    string CardholderName,
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);

/// <summary>Request to authorize (place a hold for) an order total on PayPal.</summary>
public record AuthorizeGatewayRequest(
    string ReferenceId, // becomes the PayPal invoice_id (unique per merchant)
    string CustomId,    // becomes the PayPal custom_id
    decimal Amount,
    string Currency,
    CardDetails? Card,
    string? VaultId,
    string IdempotencyKey);

/// <summary>The hold as PayPal reports it.</summary>
public record GatewayAuthorization(
    string PayPalOrderId,
    string OrderStatus,
    string? AuthorizationId,
    string? AuthorizationStatus,
    DateTimeOffset? ExpiresAt,
    bool RequiresPayerAction);

/// <summary>A capture as PayPal reports it, including the fee breakdown.</summary>
public record GatewayCapture(
    string CaptureId,
    string Status,
    decimal Gross,
    decimal PayPalFee,
    decimal Net,
    string Currency);

/// <summary>A refund as PayPal reports it.</summary>
public record GatewayRefund(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);

/// <summary>Request to vault (save) a card for a shopper.</summary>
public record VaultCardRequest(
    CardDetails Card,
    string? MerchantCustomerId,
    string? PayPalCustomerId,
    string? Alias,
    string IdempotencyKey);

/// <summary>A saved card as PayPal reports it: the vault id plus a safe descriptor only.</summary>
public record VaultedCard(
    string VaultId,
    string? PayPalCustomerId,
    string Brand,
    string Last4,
    string? Expiry,
    string? CardType,
    string? CardholderName);

/// <summary>One transaction from PayPal's own transaction reporting, used for reconciliation.</summary>
public record ReportedTransaction(
    string TransactionId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? InitiationDate,
    string? InvoiceId,
    string? CustomField,
    string? PayPalReferenceId);
