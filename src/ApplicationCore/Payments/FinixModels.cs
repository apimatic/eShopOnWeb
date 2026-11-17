using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record FinixAddress(
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string Country);

public sealed record FinixCard(
    string Name,
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string? SecurityCode,
    FinixAddress? Address);

public sealed record FinixIdentityResult(string Id);

public sealed record FinixPaymentInstrumentResult(
    string Id,
    string? Brand,
    string? LastFour,
    int ExpirationMonth,
    int ExpirationYear,
    bool Enabled);

public sealed record FinixAuthorizationResult(
    string Id,
    string State,
    int Amount,
    string Currency,
    DateTimeOffset? ExpiresAt,
    bool IsVoid,
    string? TransferId,
    string? FailureCode,
    string? FailureMessage);

public sealed record FinixTransferResult(
    string Id,
    string Type,
    string State,
    int Amount,
    int Fee,
    string? ParentTransfer,
    string? OperationKey,
    DateTimeOffset CreatedAt,
    string? Currency,
    string? FailureCode,
    string? FailureMessage);
