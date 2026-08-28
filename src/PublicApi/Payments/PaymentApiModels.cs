using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItemRequest> Items, ShippingAddressRequest ShippingAddress);
public sealed record CreateOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);

public sealed record CardRequest(string Name, string Number, string Expiry, string SecurityCode,
    BillingAddressRequest BillingAddress)
{
    public override string ToString() => "CardRequest { [REDACTED] }";
}

public sealed record BillingAddressRequest(string AddressLine1, string? AddressLine2, string City,
    string State, string PostalCode, string CountryCode);

public sealed record PayOrderRequest(int? PaymentMethodId, CardRequest? Card);
public sealed record SavePaymentMethodRequest(CardRequest? Card);
public sealed record RefundOrderRequest(string IdempotencyKey, decimal? Amount, string? Note);

public sealed record CreateOrderResponse(int OrderId, string Status, decimal Total, string Currency);
public sealed record PayOrderResponse(int OrderId, string OrderStatus, PaymentResponse Payment);
public sealed record FulfilOrderResponse(int OrderId, string OrderStatus, PaymentResponse Payment);
public sealed record CancelOrderResponse(int OrderId, string OrderStatus, PaymentResponse? Payment);
public sealed record RefundOrderResponse(int RefundId, int OrderId, string OrderStatus, PaymentResponse Payment);
public sealed record SavePaymentMethodResponse(int PaymentMethodId, string Brand, string LastDigits, string Expiry);
public sealed record PaymentMethodResponse(int PaymentMethodId, string Brand, string LastDigits, string Expiry,
    DateTimeOffset CreatedAt);

public sealed record OrderResponse(int OrderId, DateTimeOffset OrderDate, string Status, decimal Total,
    string Currency, IReadOnlyList<OrderItemResponse> Items, PaymentResponse? Payment);
public sealed record OrderItemResponse(int CatalogItemId, string ProductName, decimal UnitPrice, int Quantity);

public sealed record PaymentResponse(string Status, string? PayPalOrderId, string? AuthorizationId,
    string? AuthorizationStatus, DateTimeOffset? AuthorizationExpiresAt, string? CaptureId,
    string? CaptureStatus, decimal AuthorizedAmount, decimal? CapturedAmount, decimal? PayPalFee,
    decimal? NetAmount, decimal RefundedAmount, decimal RefundableAmount,
    IReadOnlyList<RefundResponse> Refunds);
public sealed record RefundResponse(int RefundId, string Status, decimal Amount, string Currency,
    DateTimeOffset RequestedAt, DateTimeOffset? CompletedAt);

public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationEntryResponse> Entries);
public sealed record ReconciliationEntryResponse(string MatchStatus, int? OrderId, string? OrderStatus,
    string? PayPalTransactionId, string? PayPalReferenceId, string? PayPalEventCode,
    string? PayPalStatus, decimal? Amount, string? Currency, decimal? Fee,
    DateTimeOffset? TransactionTime);
