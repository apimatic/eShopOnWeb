using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed record CardDetails(string Number, string Expiry, string SecurityCode, string Name, BillingAddress BillingAddress);
public sealed record BillingAddress(string AddressLine1, string? AddressLine2, string City, string State, string PostalCode, string CountryCode);
public sealed record PayPalAuthorization(string PayPalOrderId, string OrderStatus, string AuthorizationId, string AuthorizationStatus, decimal Amount, string Currency, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
public sealed record PayPalCapture(string Id, string Status, decimal Amount, string Currency, decimal Fee, decimal NetAmount, DateTimeOffset CreatedAt);
public sealed record PayPalRefund(string Id, string Status, decimal Amount, string Currency, DateTimeOffset CreatedAt);
public sealed record VaultedCard(string VaultId, string CustomerId, string Brand, string Last4, string Expiry, string? CardholderName);
public sealed record PayPalTransaction(string TransactionId, string? PayPalReferenceId, string? InvoiceId, string? CustomField, string EventCode, string Status, decimal Amount, decimal Fee, string Currency, DateTimeOffset InitiatedAt, DateTimeOffset UpdatedAt);

public interface IPayPalPaymentsClient
{
    Task<PayPalAuthorization> AuthorizeAsync(string paymentReference, decimal amount, CardDetails? card, string? vaultId, string requestId, CancellationToken cancellationToken);
    Task<PayPalCapture> CaptureAsync(string authorizationId, string paymentReference, decimal amount, string currency, string requestId, CancellationToken cancellationToken);
    Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken);
    Task<PayPalRefund> RefundAsync(string captureId, string paymentReference, decimal amount, string currency, string idempotencyKey, string requestId, CancellationToken cancellationToken);
    Task<VaultedCard> SaveCardAsync(string buyerId, string? payPalCustomerId, CardDetails card, string requestId, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
