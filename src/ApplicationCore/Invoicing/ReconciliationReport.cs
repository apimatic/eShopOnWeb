using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.InvoiceAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// One line of the reconciliation report: an invoice seen by the provider and/or recorded by eShop over
/// the requested range, made plain which side knows about it and whether this application raised it.
/// </summary>
public record ReconciliationEntry(
    string InvoiceId,
    bool InProviderRecords,
    bool InEShopRecords,
    bool RaisedByEShop,
    string? ProviderStatus,
    DateTimeOffset? CreatedDate,
    decimal? Amount,
    string? Currency,
    int? OrderId,
    InvoiceState? LocalState,
    string Discrepancy);

/// <summary>
/// A reconciliation report over a date range: the provider's own record of bills lined up against what
/// eShop believes it raised, so a bill one side knows about and the other does not is visible, and the
/// provider's foreign bills are not presented as though they were all eShop's.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int ProviderInvoiceCount,
    int EShopInvoiceCount,
    int MatchedCount,
    int ProviderOnlyForeignCount,
    IReadOnlyList<ReconciliationEntry> Entries);
