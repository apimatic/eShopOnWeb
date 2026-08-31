using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class PayPalCardDetails
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public required string SecurityCode { get; init; }
    public required string Name { get; init; }
    public required PayPalBillingAddress BillingAddress { get; init; }

    public override string ToString() => "[REDACTED CARD DETAILS]";
}

public sealed record PayPalBillingAddress(
    string AddressLine1,
    string? AddressLine2,
    string AdminArea2,
    string AdminArea1,
    string PostalCode,
    string CountryCode);

public sealed record PayPalPaymentSource(PayPalCardDetails? Card, string? VaultId)
{
    public static PayPalPaymentSource FromCard(PayPalCardDetails card) => new(card, null);
    public static PayPalPaymentSource FromVault(string vaultId) => new(null, vaultId);
    public override string ToString() => Card is not null ? "Card [REDACTED]" : "Saved card token";
}

public sealed record PayPalOrderCreationResult(string Id, string Status);

public sealed record PayPalAuthorizationResult(
    string PayPalOrderId,
    string PayPalOrderStatus,
    string AuthorizationId,
    string AuthorizationStatus,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? CardBrand,
    string? CardLast4);

public sealed record PayPalCaptureResult(
    string CaptureId,
    string Status,
    decimal Amount,
    string Currency,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset? CreatedAt);

public sealed record PayPalVoidResult(string AuthorizationId, string Status);

public sealed record PayPalRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? CreatedAt);

public sealed record PayPalSavedCardResult(
    string PaymentTokenId,
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
    decimal Amount,
    decimal? Fee,
    string Currency);

public sealed class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string name, string message, string? debugId,
        IReadOnlyCollection<string> issues)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        DebugId = debugId;
        Issues = issues;
    }

    public int StatusCode { get; }
    public string Name { get; }
    public string? DebugId { get; }
    public IReadOnlyCollection<string> Issues { get; }

    public bool HasIssue(string issue)
    {
        foreach (var current in Issues)
        {
            if (string.Equals(current, issue, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

public sealed class PayPalPayerActionRequiredException : Exception
{
    public PayPalPayerActionRequiredException(string operation)
        : base($"PayPal requires browser approval for {operation}; this API only supports headless direct-card payments.")
    {
    }
}
