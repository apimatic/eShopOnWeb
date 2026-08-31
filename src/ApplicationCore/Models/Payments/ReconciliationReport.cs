using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

public record ReconciliationEntry(
    string TransactionId,
    string? Type,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? InitiatedAt,
    int? MatchedOrderId,
    string? MatchedAs);

public record CaptureMissingFromPayPal(
    int OrderId,
    string CaptureId,
    decimal Amount,
    string Currency,
    DateTimeOffset CapturedAt);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Transactions,
    IReadOnlyList<CaptureMissingFromPayPal> CapturesMissingFromPayPal);
