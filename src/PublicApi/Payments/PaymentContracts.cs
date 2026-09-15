using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record MoneyDto(string Currency, decimal Value);
public sealed record CardAddressDto(string AddressLine1, string? AddressLine2, string AdminArea2, string? AdminArea1, string PostalCode, string CountryCode);
public sealed record CardDto(string Number, string Expiry, string SecurityCode, string Name, CardAddressDto BillingAddress);
public sealed record PayPalAuthorization(string PayPalOrderId, string AuthorizationId, string Status, decimal Amount, string Currency, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
public sealed record PayPalCapture(string Id, string Status, decimal Amount, string Currency, decimal? Fee, decimal? NetAmount, DateTimeOffset CreatedAt);
public sealed record PayPalRefund(string Id, string Status, decimal Amount, string Currency, DateTimeOffset CreatedAt);
public sealed record PayPalSavedCard(string TokenId, string CustomerId, string Brand, string Last4, string Expiry, string? CardholderName);
public sealed record PayPalTransaction(string TransactionId, string? ReferenceId, string? ReferenceIdType, string? EventCode, DateTimeOffset InitiatedAt, DateTimeOffset? UpdatedAt, decimal? Amount, string? Currency, decimal? Fee, string? Status, string? InvoiceId);

public sealed class PayPalException : Exception
{
    public PayPalException(int statusCode, string name, string message, string? issue, string? debugId)
        : base($"PayPal {name}: {message}{(issue == null ? string.Empty : $" ({issue})")}{(debugId == null ? string.Empty : $" [debug_id {debugId}]")}")
    { StatusCode = statusCode; Name = name; Issue = issue; DebugId = debugId; }
    public int StatusCode { get; }
    public string Name { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
}

public sealed class PaymentActionRequiredException : Exception
{
    public PaymentActionRequiredException() : base("PayPal requires browser approval for this card. This API intentionally does not implement an approval round-trip; use another card or review the merchant's card-processing configuration.") { }
}

public interface IPayPalGateway
{
    string Currency { get; }
    Task<PayPalAuthorization> AuthorizeAsync(string paymentReference, decimal amount, CardDto? card, string? vaultId, CancellationToken cancellationToken);
    Task<PayPalAuthorization> ReauthorizeAsync(string paymentReference, string authorizationId, CancellationToken cancellationToken);
    Task<PayPalCapture> CaptureAsync(string paymentReference, string authorizationId, decimal amount, CancellationToken cancellationToken);
    Task<string> VoidAsync(string paymentReference, string authorizationId, CancellationToken cancellationToken);
    Task<PayPalRefund> RefundAsync(string paymentReference, string captureId, decimal amount, string idempotencyKey, CancellationToken cancellationToken);
    Task<PayPalSavedCard> SaveCardAsync(string shopperId, string? paypalCustomerId, CardDto card, CancellationToken cancellationToken);
    Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
