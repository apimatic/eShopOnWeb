using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    /// <summary>
    /// Places an order from catalog items for the buyer. The order starts in
    /// <see cref="OrderStatus.AwaitingPayment"/>.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total, either with one-off card details or with one
    /// of the buyer's saved cards. Idempotent: paying an already-authorized order returns
    /// the existing payment.
    /// </summary>
    Task<Payment> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: captures the held funds. Renews a stale authorization when
    /// possible; throws <see cref="Exceptions.AuthorizationNotRenewableException"/> when
    /// PayPal can no longer renew it.
    /// </summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action: cancels before fulfilment, voiding the authorization so the
    /// held funds are released and no money moves. Returns the payment, or null when
    /// the order was never paid.
    /// </summary>
    Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a fulfilled order, in full (amount null) or in part. The idempotency key
    /// guarantees a repeated request never refunds twice.
    /// </summary>
    Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);
}

public sealed record OrderLine(int CatalogItemId, int Quantity);

public sealed record CardDetails(
    string Number,
    string Expiry,
    string SecurityCode,
    string? CardholderName,
    CardBillingAddress? BillingAddress);

public sealed record CardBillingAddress(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string CountryCode);
