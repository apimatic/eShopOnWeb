using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed record PaymentCardData(string Name, string Number, string Expiry, string SecurityCode,
    PaymentBillingAddress BillingAddress);

public sealed record PaymentBillingAddress(string AddressLine1, string? AddressLine2, string City,
    string? State, string PostalCode, string CountryCode);

public sealed record PayPalPaymentSource(string? VaultId, PaymentCardData? Card, string Description);

public sealed record PayPalAuthorizationResult(string PayPalOrderId, string OrderStatus,
    string AuthorizationId, string AuthorizationStatus, decimal Amount, string Currency,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

public sealed record PayPalAuthorizationDetails(string AuthorizationId, string Status, decimal Amount,
    string Currency, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);

public sealed record PayPalCaptureResult(string CaptureId, string Status, decimal Amount, string Currency,
    decimal Fee, decimal Net, DateTimeOffset CreatedAt);

public sealed record PayPalRefundResult(string RefundId, string Status, decimal Amount, string Currency,
    decimal? Fee, decimal? Net, decimal? TotalRefunded, DateTimeOffset CreatedAt);

public sealed record PayPalVaultResult(string TokenId, string Brand, string LastDigits, string Expiry);

public sealed record PayPalTransaction(string TransactionId, string? ReferenceId, string? ReferenceType,
    string? InvoiceId, string? EventCode, string? Status, decimal? Amount, decimal? Fee, string? Currency,
    DateTimeOffset? InitiatedAt, DateTimeOffset? UpdatedAt);

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string name, string detail, string? debugId)
        : base($"PayPal {name}: {detail}{(debugId is null ? string.Empty : $" (debug id {debugId})")}")
    {
        StatusCode = statusCode;
        ErrorName = name;
        DebugId = debugId;
    }

    public int StatusCode { get; }
    public string ErrorName { get; }
    public string? DebugId { get; }
}

public sealed class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string operation)
        : base($"PayPal requires browser-based payer approval for {operation}; this API intentionally does not implement an approval round-trip.") { }
}
