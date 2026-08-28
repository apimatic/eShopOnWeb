using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentApplicationService
{
    Task<int> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> items,
        ShippingAddressInput shippingAddress, CancellationToken cancellationToken);
    Task<PaymentResult> PayAsync(string buyerId, int orderId, CardInput? card, int? paymentMethodId,
        CancellationToken cancellationToken);
    Task<PaymentResult> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<PaymentResult> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<RefundResult> RefundAsync(string buyerId, int orderId, string idempotencyKey, decimal? amount,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<OrderResult>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<PaymentMethodResult> SavePaymentMethodAsync(string buyerId, CardInput card,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PaymentMethodResult>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken);
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
    Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record OrderLineInput(int CatalogItemId, int Quantity);

public sealed record ShippingAddressInput(string Street, string City, string State, string Country,
    string PostalCode);

public sealed record BillingAddressInput(string AddressLine1, string? AddressLine2, string AdminArea2,
    string? AdminArea1, string PostalCode, string CountryCode);

public sealed record CardInput(string Number, string Expiry, string SecurityCode, string Name,
    BillingAddressInput BillingAddress);

public sealed record PaymentAuthorizationResult(string Id, string Status, decimal Amount,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, bool IsReauthorization, bool IsCurrent);

public sealed record RefundResult(string RefundId, string Status, decimal Amount, string Currency,
    string IdempotencyKey);

public sealed record PaymentResult(string Status, decimal Amount, string Currency, string? PayPalOrderId,
    IReadOnlyCollection<PaymentAuthorizationResult> Authorizations, string? CaptureId,
    string? CaptureStatus, decimal? CapturedAmount, decimal? PayPalFee, decimal? NetAmount,
    decimal RefundedAmount, IReadOnlyCollection<RefundResult> Refunds);

public sealed record OrderItemResult(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record OrderResult(int OrderId, DateTimeOffset OrderDate, decimal Total, string Currency,
    string PaymentStatus, string FulfillmentStatus, ShippingAddressInput ShippingAddress,
    IReadOnlyCollection<OrderItemResult> Items, PaymentResult? Payment);

public sealed record PaymentMethodResult(int PaymentMethodId, string Brand, string Last4, string Expiry,
    string? CardholderName);

public sealed record ReconciliationTransactionResult(string TransactionId, string? PayPalReferenceId,
    string? EventCode, string? Status, decimal? Amount, decimal? Fee, string? Currency,
    DateTimeOffset? InitiatedAt, int? OrderId);

public sealed record ReconciliationLocalEntryResult(int OrderId, string Kind, string PayPalId,
    string Status, decimal Amount, string Currency, DateTimeOffset OccurredAt);

public sealed record ReconciliationResult(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyCollection<ReconciliationTransactionResult> PayPalTransactions,
    IReadOnlyCollection<ReconciliationLocalEntryResult> EshopOnly);
