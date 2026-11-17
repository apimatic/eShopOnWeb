namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Card details supplied for a single request. These values are forwarded to
/// Finix and are never persisted or logged by this application.
/// </summary>
public sealed record CardPaymentInput(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string? SecurityCode,
    string Name,
    FinixAddress? BillingAddress);
