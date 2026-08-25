using System;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>Display-safe description of a card PayPal has vaulted. Never carries a full card number.</summary>
public record SavedCard(string VaultId, string? Brand, string LastDigits, string Expiry, string? CardholderName, string PayPalCustomerId);

/// <summary>One row of PayPal's own transaction record, for reconciliation against local orders.</summary>
public record PayPalTransactionRecord(
    string TransactionId, decimal? Amount, string? Currency, string? Status,
    DateTimeOffset? InitiatedAt, DateTimeOffset? UpdatedAt, decimal? FeeAmount);
