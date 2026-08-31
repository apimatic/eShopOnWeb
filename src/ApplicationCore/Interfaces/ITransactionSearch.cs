using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record GatewayTransaction(
    string TransactionId,
    string? PayPalReferenceId,
    string? ReferenceIdType,
    string? InvoiceId,
    string? CustomField,
    string? EventCode,
    DateTimeOffset? Time,
    decimal? Amount,
    string? Currency,
    decimal? Fee,
    string? Status);

/// <summary>
/// The provider's own record of transactions, used for reconciliation. Covers the whole
/// requested range (all pages).
/// </summary>
public interface ITransactionSearch
{
    Task<IReadOnlyList<GatewayTransaction>> SearchAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
