using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string FirstName, string LastName, string Street,
    string City, string? State, string Country, string ZipCode);
public sealed record PlaceOrderRequest(IReadOnlyList<OrderLineRequest> Items, ShippingAddressRequest ShippingAddress);

public sealed record CardAddressRequest(string AddressLine1, string? AddressLine2, string City,
    string? State, string PostalCode, string CountryCode);
public sealed record CardRequestDto(string Name, string Number, string Expiry, string SecurityCode,
    CardAddressRequest BillingAddress);
public sealed record PayOrderRequest(CardRequestDto? Card, int? PaymentMethodId);
public sealed record SavePaymentMethodRequest(CardRequestDto Card);
public sealed record RefundOrderRequest(decimal? Amount, string IdempotencyKey);

public sealed record OrderCreatedResponse(int OrderId, decimal Total, string Currency, string PaymentState);
public sealed record PaymentResponse(int OrderId, string PaymentState, string? PayPalOrderId,
    string? AuthorizationId, decimal? AuthorizedAmount, string? AuthorizationStatus);
public sealed record FulfilmentResponse(int OrderId, string PaymentState, string CaptureId,
    string CaptureStatus, decimal CapturedAmount, decimal GrossAmount, decimal? PayPalFee,
    decimal? NetProceeds, string Currency);
public sealed record CancelResponse(int OrderId, string PaymentState, string AuthorizationStatus);
public sealed record RefundCreatedResponse(string RefundId, int OrderId, string Status, decimal Amount,
    string Currency, decimal RemainingRefundableAmount);
public sealed record PaymentMethodResponse(int PaymentMethodId, string? Brand, string? LastDigits,
    string? Expiry, string? CardType);
public sealed record PaymentMethodsResponse(IReadOnlyList<PaymentMethodResponse> PaymentMethods);
public sealed record RefundResponse(string? RefundId, string Status, decimal Amount, string Currency);
public sealed record OrderResponse(int OrderId, DateTimeOffset OrderDate, decimal Total, string? Currency,
    string PaymentState, string? AuthorizationId, string? AuthorizationStatus, string? CaptureId,
    string? CaptureStatus, decimal? CapturedAmount, decimal? PayPalFee, decimal? NetProceeds,
    decimal RefundableAmount, IReadOnlyList<RefundResponse> Refunds);
public sealed record MyOrdersResponse(IReadOnlyList<OrderResponse> Orders);

public sealed record ReconciliationRow(string Source, string MatchState, int? OrderId,
    string? ProviderTransactionId, string? ProviderReferenceId, string? TransactionType,
    string? Status, decimal? Amount, string? Currency, decimal? Fee, string? InvoiceId,
    string? InitiatedAt);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationRow> Rows);
