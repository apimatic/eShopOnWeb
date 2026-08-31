using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed record OrderLineInput(int CatalogItemId, int Quantity);

public sealed record ReconciliationEntry(string Source, string MatchStatus, int? OrderId,
    string? PayPalTransactionId, string? PayPalReferenceId, string? InvoiceId, string? TransactionType,
    string? Status, decimal? Amount, decimal? Fee, string? Currency, DateTimeOffset? TransactionTime);

public sealed record ReconciliationResult(DateTimeOffset From, DateTimeOffset To,
    int PayPalTransactionCount, int EShopTransactionCount, IReadOnlyList<ReconciliationEntry> Entries);

public interface ICommercePaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, Address shippingAddress,
        CancellationToken cancellationToken);
    Task<Order> AuthorizeAsync(string buyerId, int orderId, PaymentCardData? card, int? paymentMethodId,
        CancellationToken cancellationToken);
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, PaymentCardData card,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken);
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
    Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}
