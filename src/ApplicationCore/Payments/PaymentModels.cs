using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public record OrderLineInput(int CatalogItemId, int Quantity);

public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>
/// One-off card details. Never persisted, echoed, or logged by this application.
/// </summary>
public record CardInput(
    string Name,
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string? SecurityCode,
    string? Street = null,
    string? City = null,
    string? State = null,
    string? Country = null,
    string? ZipCode = null);

public record PayOrderCommand(string? PaymentMethodId, CardInput? Card);

public record PaymentSummary(
    string Status,
    string? PaymentMethodId,
    string? AuthorizationId,
    string? AuthorizationState,
    string? TransferId,
    long AuthorizedAmountCents,
    long CapturedAmountCents,
    long FeeCents,
    long NetCents,
    long RefundedAmountCents,
    string? FailureCode,
    string? FailureMessage);

public record OrderLineSummary(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

public record OrderSummary(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string PaymentStatus,
    PaymentSummary Payment,
    IReadOnlyList<OrderLineSummary> Items);

public record RefundSummary(
    string RefundId,
    int OrderId,
    long AmountCents,
    long TotalRefundedCents,
    string State);

public record SavedCardSummary(
    string PaymentMethodId,
    string? Brand,
    string? Last4,
    int? ExpirationMonth,
    int? ExpirationYear);

public record ReconciliationRow(
    string MatchStatus,
    int? OrderId,
    string? TransferId,
    string? TransferType,
    string? TransferState,
    long? AmountCents,
    long? FeeCents,
    DateTimeOffset? TransferCreatedAt,
    string? Note);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    bool Truncated,
    string? NextCursor,
    DateTimeOffset? CoveredTo,
    int ProviderTransactionCount,
    int AppOrderCount,
    IReadOnlyList<ReconciliationRow> Rows);

public class PaymentOperationException : Exception
{
    public PaymentOperationException(
        string message,
        int statusCode = 400,
        string? code = null,
        string? logRef = null,
        bool outcomeUnknown = false,
        bool duplicateIdempotency = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        LogRef = logRef;
        OutcomeUnknown = outcomeUnknown;
        DuplicateIdempotency = duplicateIdempotency;
    }

    public int StatusCode { get; }
    public string? Code { get; }
    public string? LogRef { get; }
    public bool OutcomeUnknown { get; }
    public bool DuplicateIdempotency { get; }
}
