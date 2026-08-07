using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders for a shopper and takes / refunds their PayPal payment. All operations are scoped to
/// the calling shopper (<c>buyerId</c>) and the money-moving operations are idempotent in effect: a
/// repeated pay never double-charges and a repeated refund never double-refunds.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order (awaiting payment) from catalog items for a shopper and returns the new order.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        CancellationToken cancellationToken = default);

    /// <summary>Pays an awaiting-payment order with PayPal, using either raw card details or one of the shopper's saved cards.</summary>
    Task<Order> PayOrderAsync(string buyerId, int orderId, PaymentInstruction instruction,
        CancellationToken cancellationToken = default);

    /// <summary>Refunds an order's payment in full.</summary>
    Task<Order> RefundOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// How to pay an order. Exactly one of <see cref="Card"/> (a one-off raw card) or
/// <see cref="SavedPaymentMethodId"/> (one of the shopper's saved cards) must be provided.
/// </summary>
public record PaymentInstruction(CardDetails? Card, int? SavedPaymentMethodId)
{
    public bool UsesSavedCard => SavedPaymentMethodId.HasValue;
    public bool UsesOneOffCard => Card is not null;
    public bool IsValid => UsesSavedCard ^ UsesOneOffCard;
}
