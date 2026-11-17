using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record OrderItemInput(int CatalogItemId, int Quantity);

public sealed record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

public sealed record CardAddressInput(string Line1, string City, string Region, string PostalCode, string Country);

public sealed record CardInput(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string SecurityCode,
    string Name,
    CardAddressInput? Address);

public sealed record RefundDto(
    int RefundId,
    string FinixRefundId,
    decimal Amount,
    string State,
    string IdempotencyKey,
    DateTimeOffset CreatedAt);

public sealed record OrderDto(
    int OrderId,
    DateTimeOffset OrderDate,
    string BuyerId,
    decimal Total,
    long TotalInCents,
    string? Currency,
    string PaymentStatus,
    string? AuthorizationId,
    string? AuthorizationState,
    string? CaptureId,
    string? PaymentInstrumentId,
    decimal CapturedAmount,
    decimal FeeAmount,
    decimal NetAmount,
    decimal RefundedAmount,
    string? FailureMessage,
    DateTimeOffset? AuthorizedAt,
    DateTimeOffset? FulfilledAt,
    DateTimeOffset? CanceledAt,
    IReadOnlyList<RefundDto> Refunds);

public sealed record SavedCardDto(
    int PaymentMethodId,
    string Brand,
    string Last4,
    int ExpirationMonth,
    int ExpirationYear,
    string? Name,
    DateTimeOffset CreatedAt);

public sealed record FinixTransactionDto(
    string FinixId,
    string Type,
    string State,
    long AmountInCents,
    string? Currency,
    DateTimeOffset? CreatedAt,
    string? OrderId,
    bool Matched);

public sealed record ReconciliationOrderDto(
    int OrderId,
    string BuyerId,
    string PaymentStatus,
    string? AuthorizationId,
    string? CaptureId,
    decimal CapturedAmount,
    decimal RefundedAmount,
    bool Matched);

public sealed record ReconciliationSummaryDto(
    int FinixTransactionCount,
    int EshopOrderCount,
    int MatchedTransactionCount,
    int UnmatchedFinixTransactionCount,
    int UnmatchedEshopOrderCount);

public sealed record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<FinixTransactionDto> FinixTransactions,
    IReadOnlyList<ReconciliationOrderDto> EshopOrders,
    IReadOnlyList<FinixTransactionDto> UnmatchedFinixTransactions,
    IReadOnlyList<ReconciliationOrderDto> UnmatchedEshopOrders,
    ReconciliationSummaryDto Summary);

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(string message, int statusCode = 409) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
