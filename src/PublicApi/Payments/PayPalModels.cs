using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record CardInput(string Number, string Expiry, string SecurityCode, string Name,
    CardBillingAddress BillingAddress);
public sealed record CardBillingAddress(string AddressLine1, string? AddressLine2, string AdminArea1,
    string AdminArea2, string PostalCode, string CountryCode);
public sealed record AuthorizationResult(string OrderId, string AuthorizationId, string Status,
    DateTimeOffset CreateTime, DateTimeOffset? ExpirationTime);
public sealed record CaptureResult(string Id, string Status, decimal GrossAmount, decimal Fee, decimal Net);
public sealed record RefundResult(string Id, string Status, decimal Amount, string Currency);
public sealed record VaultResult(string TokenId, string Last4, string Brand, string Expiry, string? CustomerId);
public sealed record PayPalTransaction(string TransactionId, string? InvoiceId, string EventCode,
    string Status, decimal? Amount, string? Currency, DateTimeOffset? InitiatedAt);

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string name, string message, string? issue, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        Issue = issue;
        DebugId = debugId;
    }
    public int StatusCode { get; }
    public string Name { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
}

public sealed class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException(string operation)
        : base($"PayPal required browser approval during {operation}. This headless direct-card flow cannot continue; use another card or contact PayPal about the merchant's card-processing configuration.") { }
}
