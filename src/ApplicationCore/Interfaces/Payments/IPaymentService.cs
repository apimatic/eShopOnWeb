using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Orchestrates the additive payment flows over the existing order model: placing an order
/// awaiting payment, authorizing (holding) money, fulfilling (capturing) it, cancelling
/// (releasing) it, refunding it, and managing saved cards and reconciliation. Enforces that a
/// shopper only ever acts on their own orders and cards.
/// </summary>
public interface IPaymentService
{
    Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, ShippingAddressInput? address, CancellationToken cancellationToken = default);

    Task<OrderPaymentView> AuthorizeOrderAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Operator action: capture the held funds at fulfilment, renewing a stale hold if needed.</summary>
    Task<OrderPaymentView> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: release the hold before fulfilment so no money moves.</summary>
    Task<OrderPaymentView> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<RefundResultView> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<OrderPaymentView?> GetOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<SavedCardResult> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconcile PayPal's transaction record against eShop orders for a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
