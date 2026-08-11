using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>Raw card details for a one-off payment or to be vaulted. Never persisted or logged by this app.</summary>
public record CardDetails(
    string Number,
    string ExpiryMonth,
    string ExpiryYear,
    string SecurityCode,
    string CardholderName,
    BillingAddress? BillingAddress);

/// <summary>Billing address for a card (optional; helps card processing succeed).</summary>
public record BillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2,
    string AdminArea1,
    string PostalCode,
    string CountryCode);

/// <summary>An amount in a currency, formatted to PayPal's cent-accurate decimal string at the gateway.</summary>
public record Money(string CurrencyCode, decimal Value);

/// <summary>The result of authorizing an order total (placing a hold).</summary>
public record AuthorizationResult(
    string PayPalOrderId,
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>The current state of an authorization, read back from PayPal.</summary>
public record AuthorizationState(
    string AuthorizationId,
    string Status,
    DateTimeOffset? ExpiresAt);

/// <summary>The result of capturing an authorization, carrying PayPal's own settlement figures.</summary>
public record CaptureResult(
    string CaptureId,
    string Status,
    decimal GrossAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string CurrencyCode);

/// <summary>The result of refunding a capture.</summary>
public record RefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string CurrencyCode);

/// <summary>A card successfully stored in PayPal's vault — safe display only, never the full number.</summary>
public record VaultedCard(
    string VaultId,
    string Last4,
    string? Brand,
    string? ExpiryMonth,
    string? ExpiryYear);

/// <summary>One transaction as PayPal itself records it, for reconciliation against eShop orders.</summary>
public record PayPalTransactionRecord(
    string TransactionId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    string? OrderReference,
    DateTimeOffset Date);
