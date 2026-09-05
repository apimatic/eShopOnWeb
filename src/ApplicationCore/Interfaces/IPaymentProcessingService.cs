using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One catalog item and quantity in a request to place an order.</summary>
public record PlaceOrderLine(int CatalogItemId, int Quantity);

public class PaymentOperationResult
{
    public required Order Order { get; init; }

    /// <summary>Null when the order never had a payment, e.g. it was cancelled before it was paid for.</summary>
    public OrderPayment? Payment { get; init; }

    /// <summary>Set when the request repeated something that had already happened.</summary>
    public bool AlreadyRecorded { get; init; }

    /// <summary>What the operator or shopper should know about how the money moved.</summary>
    public string? Note { get; init; }

    /// <summary>Set when a hold that had gone stale was renewed so fulfilment could go ahead.</summary>
    public bool RenewedHold { get; init; }
}

public class RefundOperationResult
{
    public required Order Order { get; init; }
    public required OrderPayment Payment { get; init; }
    public required PaymentRefund Refund { get; init; }
    public bool AlreadyRecorded { get; init; }
}

public class OrderSummary
{
    public required Order Order { get; init; }
    public OrderPayment? Payment { get; init; }
}

/// <summary>
/// Collects the money for an order: holds it at checkout, takes it at fulfilment, releases it on a
/// cancellation and gives it back on a return.
/// </summary>
public interface IPaymentProcessingService
{
    /// <summary>The currency orders are paid in, from configuration.</summary>
    string Currency { get; }

    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderLine> lines, Address shipToAddress,
        CancellationToken cancellationToken = default);

    Task<PaymentOperationResult> PayAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<PaymentOperationResult> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<PaymentOperationResult> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<RefundOperationResult> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        string? noteToPayer, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderSummary>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethod>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
