using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderLine(int CatalogItemId, int Quantity);

public record OrderAddressInput(string Street, string City, string State, string Country, string ZipCode);

public record CardPaymentInput(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    OrderAddressInput? BillingAddress);

public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        OrderAddressInput? shipTo,
        CancellationToken cancellationToken = default);

    Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentInput? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, PaymentRefund Refund)> RefundAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentInput card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
