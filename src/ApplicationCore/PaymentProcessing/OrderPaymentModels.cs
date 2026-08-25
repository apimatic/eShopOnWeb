using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

public record OrderItemRequest(int CatalogItemId, int Quantity);

public record OrderWithPayment(Order Order, Payment? Payment);

public record ReconciliationEntry(
    string? PayPalTransactionId,
    int? OrderId,
    decimal? PayPalAmount,
    decimal? EShopAmount,
    string? Currency,
    string? PayPalStatus,
    string Note);

public record ReconciliationReport(
    IReadOnlyList<ReconciliationEntry> MatchedInBoth,
    IReadOnlyList<ReconciliationEntry> PayPalOnly,
    IReadOnlyList<ReconciliationEntry> EShopOnly);
