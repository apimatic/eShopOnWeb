using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IReconciliationService
{
    Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciledTransaction> Transactions,
    IReadOnlyList<ReconciledTransaction> UnmatchedProcessorTransactions,
    IReadOnlyList<MissingPaymentRecord> PaymentsMissingFromProcessor);

public record ReconciledTransaction(
    string TransactionId,
    string? ReferenceId,
    string? EventCode,
    string? Status,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    DateTimeOffset? TransactionDate,
    int? OrderId,
    int? PaymentId,
    string? MatchType);

public record MissingPaymentRecord(
    int OrderId,
    int PaymentId,
    string ExpectedRecordType,
    string ExpectedTransactionId,
    string PaymentStatus);
