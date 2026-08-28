using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed record OrderItemRequest(int CatalogItemId, int Quantity);
public sealed record AddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record CreateOrderRequest(IReadOnlyCollection<OrderItemRequest> Items, AddressRequest ShipToAddress);

public sealed record BillingAddressRequest(string AddressLine1, string? AddressLine2, string City,
    string State, string PostalCode, string CountryCode);
public sealed record CardRequest(string Name, string Number, string Expiry, string SecurityCode,
    BillingAddressRequest BillingAddress);
public sealed record PayOrderRequest(CardRequest? Card, int? PaymentMethodId);
public sealed record SavePaymentMethodRequest(CardRequest Card);
public sealed record RefundOrderRequest(string IdempotencyKey, decimal? Amount);

public sealed record CreateOrderResponse(int OrderId, OrderResponse Order);
public sealed record SavePaymentMethodResponse(int PaymentMethodId, PaymentMethodResponse PaymentMethod);
public sealed record RefundResponse(string RefundId, string Status, decimal Amount, string Currency);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string Last4, string Expiry,
    DateTimeOffset CreatedAt);
public sealed record RefundView(string RefundId, string Status, decimal Amount, DateTimeOffset CreatedAt);
public sealed record PaymentView(string Status, decimal Amount, string Currency, string? PayPalOrderId,
    string? AuthorizationId, string? AuthorizationStatus, DateTimeOffset? AuthorizationExpiresAt,
    string? CaptureId, string? CaptureStatus, decimal? CapturedAmount, decimal? PayPalFee,
    decimal? NetProceeds, decimal RefundedAmount, decimal RefundableAmount, IReadOnlyCollection<RefundView> Refunds);
public sealed record OrderLineResponse(int CatalogItemId, string Name, decimal UnitPrice, int Quantity);
public sealed record OrderResponse(int OrderId, Guid ExternalId, DateTimeOffset OrderDate, string FulfilmentStatus,
    decimal Total, IReadOnlyCollection<OrderLineResponse> Items, PaymentView? Payment);

public sealed record ReconciliationItem(string MatchStatus, int? OrderId, string? PayPalTransactionId,
    string? PayPalReferenceId, string? EventCode, DateTimeOffset? InitiatedAt, decimal? Amount,
    string? Currency, decimal? Fee, string? PayPalStatus, string? InvoiceId);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyCollection<ReconciliationItem> Items);
