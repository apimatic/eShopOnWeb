using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IShopperCheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipTo, CancellationToken cancellationToken = default);
}

public sealed class OrderLineRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public interface IOrderPaymentService
{
    Task<Order> AuthorizePaymentAsync(int orderId, string buyerId, CardPaymentRequest? card, int? paymentMethodId, CancellationToken cancellationToken = default);
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<PaymentRefund> RefundOrderAsync(int orderId, string buyerId, string idempotencyKey, decimal? amount, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<Order> GetBuyerOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}

public sealed class CardPaymentRequest
{
    public required string Number { get; init; }
    public required string Expiry { get; init; }
    public string? SecurityCode { get; init; }
    public string? Name { get; init; }
    public BillingAddressRequest? BillingAddress { get; init; }
}

public sealed class BillingAddressRequest
{
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? AdminArea2 { get; init; }
    public string? AdminArea1 { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; }
}

public interface IShopperPaymentMethodService
{
    Task<ShopperPaymentMethod> SaveCardAsync(string buyerId, CardPaymentRequest card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShopperPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}

public interface IPaymentReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(System.DateTimeOffset from, System.DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class ReconciliationReport
{
    public required System.DateTimeOffset From { get; init; }
    public required System.DateTimeOffset To { get; init; }
    public IReadOnlyList<ReconciliationMatch> Matches { get; init; } = System.Array.Empty<ReconciliationMatch>();
    public IReadOnlyList<ReconciliationPayPalOnly> PayPalOnly { get; init; } = System.Array.Empty<ReconciliationPayPalOnly>();
    public IReadOnlyList<ReconciliationEshopOnly> EshopOnly { get; init; } = System.Array.Empty<ReconciliationEshopOnly>();
}

public sealed class ReconciliationMatch
{
    public int OrderId { get; init; }
    public string? PayPalTransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public string? EshopPaymentId { get; init; }
    public string? Status { get; init; }
}

public sealed class ReconciliationPayPalOnly
{
    public string? PayPalTransactionId { get; init; }
    public string? PayPalReferenceId { get; init; }
    public string? InvoiceId { get; init; }
    public string? CustomField { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? Status { get; init; }
    public string? EventCode { get; init; }
}

public sealed class ReconciliationEshopOnly
{
    public int OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? PayPalOrderId { get; init; }
    public string? AuthorizationId { get; init; }
    public string? CaptureId { get; init; }
    public decimal Amount { get; init; }
}
