using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Application orchestration for the pay/fulfil/cancel/refund flows and saved cards. Owns ownership
/// scoping and the payment state machine; delegates all PayPal interaction to <see cref="IPayPalGateway"/>.
/// </summary>
public interface IPaymentService
{
    /// <summary>Place an order from catalog items for the caller and open a payment awaiting funds. Returns the order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items, ShipToAddressInput? shipToAddress, CancellationToken cancellationToken);

    /// <summary>Authorize (hold) the order total using a one-off card or one of the caller's saved cards.</summary>
    Task<PaymentView> PayAsync(string buyerId, int orderId, CardInput? card, int? savedPaymentMethodId, CancellationToken cancellationToken);

    /// <summary>Operator action: fulfil the order and capture the funds.</summary>
    Task<PaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator action: cancel before fulfilment and release the held funds.</summary>
    Task<PaymentView> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Refund a fulfilled order in full or in part, under a caller idempotency key.</summary>
    Task<RefundView> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Operator action: reconcile PayPal's transaction record against eShop orders for a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>Save (vault) a card for the caller.</summary>
    Task<SavedCardView> SaveCardAsync(string buyerId, CardInput card, CancellationToken cancellationToken);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> GetMyCardsAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Remove a saved card so it no longer appears and can no longer be used to pay.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}
