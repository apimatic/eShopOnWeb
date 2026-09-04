using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PlaceOrderLine(int CatalogItemId, int Quantity);

/// <summary>Card details for a one-off payment (never persisted) — or the id of a saved card.</summary>
public sealed record PayCommand(
    CardCredential? Card,
    string? SavedPaymentMethodId,
    decimal? ExpectedAmount);

public sealed record RefundCommand(
    decimal? Amount,
    string IdempotencyKey);

public sealed record PlaceOrderResult(Order Order);
public sealed record PayResult(Order Order, PaymentDetails Payment, bool Replayed);
public sealed record FulfilResult(Order Order, PaymentDetails Payment, bool Replayed);
public sealed record CancelResult(Order Order, bool FundsReleased, bool Replayed);
public sealed record RefundResult(Order Order, PaymentRefund Refund, decimal RemainingRefundableAmount, bool Replayed);

public interface IOrderPaymentService
{
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderLine> lines, Address? shipToAddress, CancellationToken ct = default);

    Task<PayResult> PayAsync(int orderId, string buyerId, PayCommand command, CancellationToken ct = default);

    Task<FulfilResult> FulfilAsync(int orderId, CancellationToken ct = default);

    Task<CancelResult> CancelAsync(int orderId, CancellationToken ct = default);

    Task<RefundResult> RefundAsync(int orderId, string callerId, bool callerIsAdmin, RefundCommand command, CancellationToken ct = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default);

    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardCredential card, CancellationToken ct = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId, CancellationToken ct = default);

    Task DeleteCardAsync(string buyerId, string paymentMethodId, CancellationToken ct = default);
}
