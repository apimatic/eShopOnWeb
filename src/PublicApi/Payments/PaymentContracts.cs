using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddress BillingAddress { get; set; } = new();
}

public sealed class BillingAddress
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string AdminArea1 { get; set; } = string.Empty;
    public string AdminArea2 { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}

public sealed record PayPalAuthorizationResult(
    string OrderId,
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    decimal? Net,
    DateTimeOffset CreatedAt);

public sealed record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record VaultedCardResult(
    string VaultId,
    string CustomerId,
    string Brand,
    string Last4,
    string Expiry);

public sealed record PayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string? ReferenceIdType,
    string EventCode,
    string Status,
    DateTimeOffset InitiatedAt,
    DateTimeOffset? UpdatedAt,
    decimal? Amount,
    decimal? Fee,
    string? Currency);
