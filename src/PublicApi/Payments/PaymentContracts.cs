using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record AddressRequest(string Street, string City, string State, string Country,
    string ZipCode);
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineRequest> Items,
    AddressRequest ShippingAddress);

public sealed record CardRequest(string Name, string Number, string Expiry, string SecurityCode,
    BillingAddressRequest BillingAddress);
public sealed record BillingAddressRequest(string CountryCode, string? AddressLine1,
    string? AddressLine2, string? City, string? State, string? PostalCode);
public sealed record PayOrderRequest(CardRequest? Card, int? PaymentMethodId);
public sealed record RefundOrderRequest(decimal? Amount, string IdempotencyKey);
public sealed record SavePaymentMethodRequest(CardRequest Card);

public sealed record OrderCreatedResponse(int OrderId, string PaymentStatus, decimal Total,
    string Currency);
public sealed record OrderActionResponse(int OrderId, string PaymentStatus, string FulfilmentStatus,
    decimal Total, string Currency, string? PaypalAuthorizationId, string? PaypalCaptureId,
    decimal? CapturedAmount, decimal? PaypalFee, decimal? NetProceeds, decimal RefundedAmount);
public sealed record RefundCreatedResponse(int RefundId, int OrderId, string PaypalRefundId,
    string Status, decimal Amount, string Currency);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string LastDigits,
    string Expiry, DateTimeOffset CreatedAt);

public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice,
    int Quantity);
public sealed record RefundResponse(int RefundId, string PaypalRefundId, string Status,
    decimal Amount, string Currency, DateTimeOffset CreatedAt);
public sealed record MyOrderResponse(int OrderId, DateTimeOffset OrderDate, decimal Total,
    string? Currency, string PaymentStatus, string FulfilmentStatus,
    string? PaypalAuthorizationStatus, string? PaypalCaptureStatus, decimal? CapturedAmount,
    decimal? PaypalFee, decimal? NetProceeds, decimal RefundedAmount,
    IReadOnlyList<OrderItemResponse> Items, IReadOnlyList<RefundResponse> Refunds);

public sealed record ReconciliationTransactionResponse(string TransactionId,
    string? PaypalReferenceId, string EventCode, string Status, decimal Amount, string Currency,
    decimal? Fee, DateTimeOffset InitiatedAt, int? OrderId, string MatchStatus);
public sealed record EshopOnlyTransactionResponse(string PaypalTransactionId, string Kind,
    int OrderId, decimal Amount, string Currency, DateTimeOffset OccurredAt);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationTransactionResponse> PaypalTransactions,
    IReadOnlyList<EshopOnlyTransactionResponse> EshopOnlyTransactions);
