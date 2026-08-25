using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

public record PayPalTransaction(
    string TransactionId,
    string? Status,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt);
