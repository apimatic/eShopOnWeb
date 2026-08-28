using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record PlaceOrderRequest(
    [param: Required, MinLength(1)] IReadOnlyList<PlaceOrderItemRequest> Items,
    [param: Required] ShippingAddressRequest ShippingAddress);

public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);

public sealed record ShippingAddressRequest(
    [param: Required] string Street,
    [param: Required] string City,
    string State,
    [param: Required] string Country,
    [param: Required] string ZipCode);

public sealed record PayOrderRequest(CardRequestDto? Card, string? PaymentMethodId);

public sealed record CardRequestDto(
    [param: Required] string Name,
    [param: Required] string Number,
    [param: Required] string Expiry,
    [param: Required] string SecurityCode,
    [param: Required] CardBillingAddressDto BillingAddress);

public sealed record CardBillingAddressDto(
    [param: Required] string AddressLine1,
    string? AddressLine2,
    [param: Required] string City,
    string State,
    [param: Required] string PostalCode,
    [param: Required] string CountryCode);

public sealed record SavePaymentMethodRequest([param: Required] CardRequestDto Card);
public sealed record RefundOrderRequest([param: Required] string IdempotencyKey, decimal? Amount);

public sealed record CreateOrderResponse(int OrderId, string PaymentState, decimal Total, string Currency);
public sealed record PayOrderResponse(int OrderId, string PaymentState, string? AuthorizationId,
    string? AuthorizationStatus, decimal? AuthorizedAmount, string Currency, DateTimeOffset? ExpiresAt);
public sealed record FulfilOrderResponse(int OrderId, string PaymentState, DateTimeOffset? FulfilledAt,
    string? CaptureId, string? CaptureStatus, decimal? CapturedAmount, decimal? PayPalFee,
    decimal? NetProceeds, string Currency);
public sealed record CancelOrderResponse(int OrderId, string PaymentState, string? AuthorizationStatus);
public sealed record RefundOrderResponse(string RefundId, int OrderId, string Status, decimal Amount,
    string Currency, decimal RemainingRefundableAmount);
public sealed record PaymentMethodResponse(string PaymentMethodId, string? Brand, string? LastDigits,
    string? Expiry, string? Name);

public sealed record MyOrderResponse(int OrderId, DateTimeOffset OrderDate, decimal Total, string Currency,
    string PaymentState, string? AuthorizationStatus, string? CaptureStatus, decimal? CapturedAmount,
    decimal? PayPalFee, decimal? NetProceeds, decimal RefundedAmount,
    IReadOnlyList<MyOrderItemResponse> Items, IReadOnlyList<MyRefundResponse> Refunds);
public sealed record MyOrderItemResponse(int CatalogItemId, string Name, decimal UnitPrice, int Quantity);
public sealed record MyRefundResponse(string? RefundId, string Status, decimal Amount, DateTimeOffset CreatedAt);

public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    bool ProviderDataAvailable, bool ReportingLagLikely, DateTimeOffset? PayPalLastRefreshedAt,
    int PayPalPagesRead, IReadOnlyList<ReconciliationMatch> Matched,
    IReadOnlyList<PayPalTransactionRecord> PayPalOnly, IReadOnlyList<LocalPaymentRecord> EShopOnly);
public sealed record ReconciliationMatch(int OrderId, string LocalKind, string LocalProviderId,
    PayPalTransactionRecord PayPal);
public sealed record LocalPaymentRecord(int OrderId, string Kind, string ProviderId, decimal Amount,
    string Currency, string Status, DateTimeOffset? UpdatedAt);
