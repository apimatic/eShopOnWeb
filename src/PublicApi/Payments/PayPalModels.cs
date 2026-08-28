using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record CardInput(
    string Name,
    string Number,
    string Expiry,
    string SecurityCode,
    BillingAddressInput BillingAddress);

public sealed record BillingAddressInput(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? State,
    string PostalCode,
    string CountryCode);

public sealed record PayPalAuthorization(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    bool PayerActionRequired);

public sealed record PayPalCapture(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    decimal? Net,
    DateTimeOffset CreatedAt);

public sealed record PayPalRefund(
    string Id,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record PayPalPaymentToken(
    string Id,
    string Brand,
    string Last4,
    string? Expiry);

public sealed record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string? EventCode,
    DateTimeOffset? InitiatedAt,
    DateTimeOffset? UpdatedAt,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? Status,
    string? InvoiceId,
    string? CustomField);

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string name, string message, string? debugId,
        IReadOnlyList<string> issues)
        : base($"PayPal {name}: {message}" +
               (issues.Count == 0 ? string.Empty : $" ({string.Join(", ", issues)})") +
               (debugId is null ? string.Empty : $" [debug id: {debugId}]"))
    {
        StatusCode = statusCode;
        ErrorName = name;
        DebugId = debugId;
        Issues = issues;
    }

    public int StatusCode { get; }
    public string ErrorName { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }
}

public sealed class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException()
        : base("PayPal requires a browser challenge for this card. This API intentionally does not implement an approval round-trip.") { }
}
