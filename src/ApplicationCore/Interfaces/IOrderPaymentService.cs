using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One catalog line requested when placing an order.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Optional ship-to address supplied when placing an order.</summary>
public record ShippingAddressInput(string Street, string City, string State, string Country, string ZipCode);

/// <summary>Billing address for a one-off card.</summary>
public record BillingAddressInput(string? Line1, string? Line2, string? City, string? State, string? PostalCode, string? CountryCode);

/// <summary>Raw card details for a one-off payment. Held only in-flight; never persisted or logged.</summary>
public record CardInput(string Number, string Expiry, string SecurityCode, string? Name, BillingAddressInput? BillingAddress);

/// <summary>
/// How to pay an order: either a one-off <see cref="Card"/> (optionally saved via <see cref="SaveCard"/>)
/// or one of the shopper's <see cref="SavedCardId"/> vaulted cards. Exactly one must be supplied.
/// </summary>
public record PaymentInstrument
{
    public CardInput? Card { get; init; }
    public int? SavedCardId { get; init; }
    public bool SaveCard { get; init; }
}

/// <summary>
/// Orchestrates the money-movement lifecycle of an order on top of PayPal: place, authorize (hold),
/// fulfil (capture), cancel (void) and refund. Each step is individually invocable and idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order for the shopper from catalog items. The order starts awaiting payment.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, ShippingAddressInput? shipTo, CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold) the order total. Shopper-scoped: only the order's owner may pay it.</summary>
    Task<Order> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfil the order and capture the money, renewing a stale hold if needed.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancel before fulfilment and release the held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shopper-scoped: refund the caller's own captured order, fully or partially, under an idempotency key.
    /// Returns the refreshed order; the specific refund is found on its payment by the same key.
    /// </summary>
    Task<Order> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);
}
