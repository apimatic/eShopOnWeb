using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineRequest> Items, ShippingAddressRequest ShippingAddress);
public sealed record PlaceOrderResponse(int OrderId, decimal Total, string Currency, string PaymentState);

public sealed record BillingAddressRequest(string AddressLine1, string? AddressLine2, string City,
    string State, string PostalCode, string CountryCode);
public sealed record CardRequestDto(string Name, string Number, string Expiry, string SecurityCode,
    BillingAddressRequest BillingAddress);
public sealed record PayOrderRequest(CardRequestDto? Card, int? PaymentMethodId);

public sealed record PaymentResponse(int OrderId, string PaymentState, string? PayPalOrderId,
    string? AuthorizationId, string? AuthorizationStatus, decimal? AuthorizedAmount,
    string Currency, string? CaptureId, string? CaptureStatus, decimal? CapturedAmount,
    decimal? PayPalFee, decimal? NetAmount, decimal RefundedAmount);

public sealed record RefundRequestDto(decimal? Amount, string IdempotencyKey);
public sealed record RefundResponse(int RefundId, string? PayPalRefundId, string Status,
    decimal Amount, string Currency, decimal RemainingRefundableAmount);

public sealed record SavePaymentMethodRequest(CardRequestDto Card);
public sealed record PaymentMethodResponse(int PaymentMethodId, string? Brand, string? LastDigits,
    string? Expiry, string? CardType);
public sealed record SavePaymentMethodResponse(int PaymentMethodId, string? Brand, string? LastDigits,
    string? Expiry, string? CardType);

public sealed record OrderItemResponse(int CatalogItemId, string ProductName, int Quantity, decimal UnitPrice);
public sealed record MyOrderResponse(int OrderId, DateTimeOffset OrderDate, decimal Total, string? Currency,
    string PaymentState, string FulfilmentState, IReadOnlyList<OrderItemResponse> Items,
    PaymentResponse? Payment);

public sealed record ReconciliationEntry(string Source, string? PayPalTransactionId, int? OrderId,
    string Kind, string? Status, decimal? Amount, decimal? Fee, string? Currency,
    DateTimeOffset? TransactionTime, string MatchState);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    DateTimeOffset? LastRefreshedAt, IReadOnlyList<ReconciliationEntry> Entries);

public sealed record ProviderCard(string Name, string Number, string Expiry, string SecurityCode,
    BillingAddressRequest BillingAddress);
public sealed record ProviderAuthorization(string PayPalOrderId, string PayPalOrderStatus,
    string AuthorizationId, string AuthorizationStatus, decimal Amount,
    DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? UpdatedAt,
    string? ResponseCode, string? AvsCode, string? CvvCode);
public sealed record ProviderCapture(string AuthorizationId, string AuthorizationStatus,
    string CaptureId, string CaptureStatus, decimal Amount, decimal? Fee, decimal? Net,
    DateTimeOffset? CapturedAt, string? ResponseCode, string? AvsCode, string? CvvCode);
public sealed record ProviderVoid(string AuthorizationId, string Status, DateTimeOffset? UpdatedAt);
public sealed record ProviderRefund(string RefundId, string Status, decimal Amount,
    DateTimeOffset? UpdatedAt);
public sealed record ProviderPaymentMethod(string TokenId, string? CustomerId, string? Brand,
    string? LastDigits, string? Expiry, string? CardType);
public sealed record ProviderTransaction(string TransactionId, string? ReferenceId,
    string? InvoiceId, string? CustomField, string? EventCode, string? Status,
    decimal? Amount, decimal? Fee, string? Currency, DateTimeOffset? InitiatedAt);
public sealed record ProviderTransactionReport(IReadOnlyList<ProviderTransaction> Transactions,
    DateTimeOffset? LastRefreshedAt);
