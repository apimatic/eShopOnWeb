using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

public record PayPalAuthorizationResult(
    string? PayPalOrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset? ExpiresAt);
