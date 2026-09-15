using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State,
    string Country, string ZipCode);
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineRequest> Items,
    ShippingAddressRequest ShipToAddress);
public sealed record PlaceOrderResponse(int OrderId, string PaymentStatus, decimal Total, string Currency);

public sealed record CardRequest(string Name, string Number, string Expiry, string SecurityCode,
    string CountryCode, string? AddressLine1, string? AddressLine2, string? City,
    string? State, string? PostalCode);
public sealed record PayOrderRequest(CardRequest? Card, int? PaymentMethodId);

public sealed record SavePaymentMethodRequest(CardRequest Card, string? Alias);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string Last4,
    string Expiry, string? Alias);

public sealed record RefundOrderRequest(string IdempotencyKey, decimal? Amount);

public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice,
    int Quantity, decimal LineTotal);
public sealed record RefundResponse(string RefundId, string Status, decimal Amount,
    DateTimeOffset? CreatedAt, string IdempotencyKey);
public sealed record PaymentResponse(string Status, string? PayPalOrderId,
    string? AuthorizationId, string? AuthorizationStatus, decimal? AuthorizedAmount,
    string? Currency, DateTimeOffset? AuthorizationExpiresAt, string? CaptureId,
    string? CaptureStatus, decimal? CapturedAmount, decimal? PayPalFee, decimal? NetAmount,
    decimal RefundedAmount, IReadOnlyList<RefundResponse> Refunds);
public sealed record OrderResponse(int OrderId, DateTimeOffset OrderDate, decimal Total,
    string PaymentStatus, string FulfilmentStatus, IReadOnlyList<OrderItemResponse> Items,
    PaymentResponse Payment);

public sealed record RefundCreatedResponse(string RefundId, string Status, decimal Amount,
    decimal RemainingRefundableAmount);

public sealed record ReconciliationTransactionResponse(string TransactionId,
    string? PayPalReferenceId, string? EventCode, string? Status, DateTimeOffset? InitiatedAt,
    decimal? Amount, string? Currency, decimal? Fee, string? InvoiceId, string? CustomField);
public sealed record ReconciliationEntryResponse(int? OrderId, string MatchStatus,
    decimal? OrderTotal, string? PaymentStatus, string? CaptureId,
    IReadOnlyList<ReconciliationTransactionResponse> PayPalTransactions);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    int PayPalTransactionCount, IReadOnlyList<ReconciliationEntryResponse> Entries);
