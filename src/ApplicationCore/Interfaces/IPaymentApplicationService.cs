using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog line an order is placed from.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>An optional shipping address for a placed order.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>How the shopper chooses to pay: raw card, or one of their saved cards.</summary>
public record PaymentInstrumentInput(CardDetails? Card, string? PaymentMethodId, BillingAddress? BillingAddress);

/// <summary>An order paired with its payment state (which may be absent until the order is paid).</summary>
public record OrderWithPayment(Order Order, OrderPayment? Payment);

/// <summary>The result of a refund: the updated payment and the refund that was created.</summary>
public record RefundOutcome(OrderPayment Payment, PaymentRefund Refund);

/// <summary>
/// Orchestrates the additive payment capability over eShop's existing order model: place, pay (authorize),
/// fulfil (capture), cancel (void), refund, saved cards, and reconciliation. Enforces per-shopper ownership
/// and idempotent effects. PayPal specifics live behind <see cref="IPaymentProcessor"/>.
/// </summary>
public interface IPaymentApplicationService
{
    /// <summary>Places an order from catalog items for the shopper. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput? shipTo, CancellationToken cancellationToken);

    /// <summary>Authorizes (holds) the order total. Idempotent: a double-click never authorizes twice.</summary>
    Task<OrderPayment> PayAsync(string buyerId, int orderId, PaymentInstrumentInput instrument,
        CancellationToken cancellationToken);

    /// <summary>Operator action: fulfils the order, capturing the held funds (renewing a stale hold first).</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator/shopper action: cancels before fulfilment, releasing the held funds.</summary>
    Task<OrderPayment> CancelAsync(string? buyerId, int orderId, CancellationToken cancellationToken);

    /// <summary>Refunds a fulfilled order, in full or in part, under a caller-supplied idempotency key.</summary>
    Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>The shopper's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Loads a single payment the shopper owns, or null if not theirs / not found.</summary>
    Task<OrderPayment?> GetPaymentForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken);

    /// <summary>Saves a card for the shopper and returns the saved-card record.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, BillingAddress? billingAddress,
        CancellationToken cancellationToken);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetCardsForBuyerAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Removes one of the shopper's saved cards, so it can no longer be used to pay.</summary>
    Task DeleteCardAsync(string buyerId, string paymentMethodId, CancellationToken cancellationToken);

    /// <summary>Operator action: reconciles PayPal's transactions against eShop orders over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
