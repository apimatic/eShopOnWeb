using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public sealed record BillingAddress(string Line1, string? Line2, string City, string Region, string PostalCode, string Country);

public sealed record CardDetails(string Number, int ExpirationMonth, int ExpirationYear, string SecurityCode,
    string Name, BillingAddress BillingAddress)
{
    public string Last4 => Number.Length >= 4 ? Number[^4..] : Number;

    public override string ToString() => $"Card ending {Last4}";
}

public sealed record SavedCardResult(string PaymentInstrumentId, string? Brand, string? Last4,
    int? ExpirationMonth, int? ExpirationYear);

public sealed record FinixAuthorizationResult(string AuthorizationId, string? State, bool IsVoid,
    long AmountMinor, DateTimeOffset? ExpiresAt, string? FailureCode, string? FailureMessage, string? TransferId);

public sealed record FinixCaptureResult(string AuthorizationId, string TransferId, string? State,
    long AmountMinor, long FeeMinor, long NetMinor, string? Currency);

public sealed record FinixRefundResult(string TransferId, string? State, string? Type,
    long AmountMinor, long FeeMinor, string? Currency);

public sealed record FinixTransactionRecord(string Id, string Kind, long AmountMinor, string? Currency,
    string? State, string? Type, DateTimeOffset? CreatedAt, string? Merchant);
