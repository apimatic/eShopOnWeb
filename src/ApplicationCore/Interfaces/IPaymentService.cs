using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One line of an order-placement request: a catalog item and how many of it.</summary>
public record PlaceOrderItem(int CatalogItemId, int Quantity);

/// <summary>Optional ship-to address for a placed order.</summary>
public record ShippingAddressInfo(string Street, string City, string State, string Country, string ZipCode);

/// <summary>Raw card + billing details for a one-off payment. Never stored or logged.</summary>
public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? CardholderName,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode);

/// <summary>
/// How to pay an order: either one-off <see cref="Card"/> details, or the id of one of the shopper's
/// <see cref="SavedCardId"/> saved cards. Exactly one must be supplied.
/// </summary>
public record PayRequestCommand(CardDetails? Card, int? SavedCardId);

/// <summary>
/// Orchestrates the money movement for orders: place, authorize (hold), fulfil (capture),
/// cancel (void) and refund. Each step is separately invocable and idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order from catalog items for the shopper; it starts awaiting payment.</summary>
    Task<int> PlaceOrderAsync(
        string buyerId, IReadOnlyCollection<PlaceOrderItem> items, ShippingAddressInfo? shipTo,
        CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total against the given card or saved card.</summary>
    Task<Payment> PayAsync(
        string buyerId, int orderId, PayRequestCommand command, CancellationToken cancellationToken = default);

    /// <summary>Operator action: captures the held funds, renewing a stale hold first if needed.</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: voids the hold before fulfilment so no money moves.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full or in part, guarded by an idempotency key.</summary>
    Task<(Payment Payment, Refund Refund)> RefundAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state attached.</summary>
    Task<IReadOnlyList<(Order Order, Payment? Payment)>> GetMyOrdersAsync(
        string buyerId, CancellationToken cancellationToken = default);
}
