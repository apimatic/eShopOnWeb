using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One line of a placed order: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay an order: either raw card details for a one-off payment, or the id of one of the
/// shopper's saved cards. Exactly one must be supplied.
/// </summary>
public class PaymentInstrument
{
    public CardDetails? Card { get; init; }
    public int? SavedPaymentMethodId { get; init; }
}

/// <summary>An order paired with its payment, for read views such as my-orders.</summary>
public record OrderWithPayment(Order Order, Payment? Payment);

/// <summary>The result of fulfilling an order: what PayPal reported for the capture.</summary>
public record FulfilmentResult(string CaptureId, decimal CapturedAmount, decimal PayPalFee, decimal NetAmount, string Currency);

/// <summary>One order lined up against PayPal's record of it during reconciliation.</summary>
public record ReconciliationEntry(
    string TransactionId,
    string Kind,               // "capture" or "refund"
    decimal Amount,
    string Currency,
    int? OrderId,              // the eShop order, when matched
    bool InPayPal,
    bool InEShop);

/// <summary>
/// A reconciliation report over a date range: transactions PayPal and eShop agree on, transactions
/// PayPal knows about that eShop does not, and eShop transactions PayPal has not (yet) reported.
/// </summary>
public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Matched,
    IReadOnlyList<ReconciliationEntry> InPayPalNotEShop,
    IReadOnlyList<ReconciliationEntry> InEShopNotPayPal);
