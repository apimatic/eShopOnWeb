using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItemRequest> Items,
    ShippingAddressRequest? ShippingAddress);
public sealed record CreateOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State,
    string Country, string ZipCode);

public sealed record PayOrderRequest(CardRequest? Card, int? PaymentMethodId);
public sealed record CardRequest(string Name, string Number, string Expiry, string SecurityCode,
    BillingAddressRequest? BillingAddress);
public sealed record BillingAddressRequest(string AddressLine1, string? AddressLine2, string City,
    string State, string PostalCode, string CountryCode);

public sealed record RefundOrderRequest(decimal? Amount, string IdempotencyKey);
public sealed record SavePaymentMethodRequest(CardRequest Card);

public sealed record CreateOrderResponse(int OrderId, string PaymentStatus, decimal Total, string Currency);
public sealed record PayOrderResponse(int OrderId, string PaymentStatus, string? PayPalAuthorizationId,
    decimal Total, string Currency);
public sealed record FulfilOrderResponse(int OrderId, string PaymentStatus, string FulfillmentStatus,
    string? PayPalCaptureId, decimal? CapturedAmount, decimal? PayPalFee, decimal? NetProceeds,
    string Currency);
public sealed record CancelOrderResponse(int OrderId, string PaymentStatus, string FulfillmentStatus);
public sealed record RefundOrderResponse(string RefundId, int OrderId, string Status, decimal Amount,
    decimal RefundedAmount, decimal RefundableAmount, string Currency);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string LastDigits,
    string Expiry, DateTimeOffset CreatedAt);

public sealed record OrderResponse(int OrderId, DateTimeOffset OrderDate, decimal Total, string Currency,
    string PaymentStatus, string FulfillmentStatus, string? PayPalAuthorizationId,
    string? PayPalCaptureId, decimal? CapturedAmount, decimal? PayPalFee, decimal? NetProceeds,
    decimal RefundedAmount, decimal RefundableAmount, IReadOnlyList<OrderItemResponse> Items,
    IReadOnlyList<RefundResponse> Refunds);
public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);
public sealed record RefundResponse(string RefundId, string Status, decimal Amount,
    string Currency, DateTimeOffset CreatedAt);

public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationEntry> Entries);
public sealed record ReconciliationEntry(string MatchStatus, int? OrderId, string? PaymentStatus,
    string? PayPalTransactionId, string? PayPalReferenceId, string? EventCode,
    decimal? PayPalAmount, decimal? PayPalFee, string? Currency, DateTimeOffset? TransactionDate,
    string? InvoiceId);
