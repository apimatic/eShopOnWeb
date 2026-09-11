using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item and quantity a shopper wants to order, drawn from the catalog.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address supplied when placing an order.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>An order together with its payment state, for the my-orders view.</summary>
public record OrderWithPayment(Order Order, OrderPayment? Payment);

/// <summary>One line of the reconciliation report, lining a PayPal transaction and/or an
/// eShop payment record up against each other.</summary>
public record ReconciliationLine(
    string? PayPalTransactionId,
    string? EventCode,
    string? Status,
    decimal? PayPalAmount,
    string? CurrencyCode,
    decimal? FeeAmount,
    DateTimeOffset? InitiationDate,
    int? OrderId,
    string MatchState);   // Matched | PayPalOnly | EShopOnly

public record ReconciliationReport(
    DateTimeOffset From,
    DateTimeOffset To,
    int PayPalTransactionCount,
    int MatchedCount,
    int PayPalOnlyCount,
    int EShopOnlyCount,
    IReadOnlyList<ReconciliationLine> Lines);

/// <summary>
/// Orchestrates the money movement that follows an order: placing it, holding the funds,
/// taking them at fulfilment, releasing them on cancel, and returning them on refund. Every
/// action is separately invocable and idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    Task<OrderPayment> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, ShippingAddressInput? shipTo, CancellationToken ct = default);

    Task<OrderPayment> AuthorizeAsync(string buyerId, int orderId, PaymentSourceInstruction source, CancellationToken ct = default);

    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct = default);

    Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct = default);

    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
