using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

public record ReconciliationEntry(
    string TransactionId,
    string Type,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset? Timestamp,
    int? OrderId);

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> MissingFromPayPal,
    IReadOnlyList<ReconciliationEntry> MissingFromEShop);
