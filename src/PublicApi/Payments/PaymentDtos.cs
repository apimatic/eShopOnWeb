using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record AddressDto(string Street, string City, string State, string Country, string ZipCode);
public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
public sealed record CreateOrderRequest(IReadOnlyList<OrderLineRequest> Items, AddressDto ShippingAddress);
public sealed record CreateOrderResponse(int OrderId, decimal Total, string Currency, string PaymentState);

public sealed record CardRequestDto(string Name, string Number, string Expiry, string SecurityCode,
    AddressDto BillingAddress);
public sealed record PayOrderRequest(CardRequestDto? Card, int? PaymentMethodId);
public sealed record RefundOrderRequest(decimal? Amount, string IdempotencyKey);

public sealed record SavePaymentMethodRequest(CardRequestDto Card);
public sealed record PaymentMethodResponse(int PaymentMethodId, string? Brand, string Last4,
    string? Expiry, string Status);
public sealed record SavePaymentMethodResponse(int PaymentMethodId, string? Brand, string Last4,
    string? Expiry, string Status);

public sealed record RefundResponse(int RefundId, string? PayPalRefundId, decimal Amount, string Status);
public sealed record PaymentResponse(int OrderId, string PaymentState, string? PayPalOrderId,
    string? AuthorizationId, string? AuthorizationStatus, string? CaptureId, string? CaptureStatus,
    decimal? CapturedAmount, decimal? PayPalFee, decimal? NetProceeds, decimal RefundedAmount,
    string? Currency, string? FailureCode, string? FailureMessage);
public sealed record OrderResponse(int OrderId, DateTimeOffset OrderDate, decimal Total,
    PaymentResponse Payment);

public sealed record ReconciliationRow(string Classification, string? TransactionId, int? OrderId,
    string? ProviderStatus, string? LocalState, string? Amount, string? Currency,
    bool PendingReporting, string? Difference);
public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    IReadOnlyList<ReconciliationRow> Rows, int ProviderPagesRead);
