using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CreateOrderRequest
{
    public List<CreateOrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShippingAddress { get; set; }
}

public sealed class CreateOrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardInput? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public sealed class SavePaymentMethodRequest
{
    public CardInput? Card { get; set; }
}

public sealed class CreateRefundRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record CreateOrderResponse(int OrderId, OrderResponse Order);
public sealed record PayOrderResponse(int OrderId, PaymentResponse Payment);
public sealed record RefundResponse(string RefundId, int OrderId, PaymentRefundResponse Refund, PaymentResponse Payment);
public sealed record SavePaymentMethodResponse(int PaymentMethodId, PaymentMethodResponse PaymentMethod);
public sealed record MyOrdersResponse(IReadOnlyList<OrderResponse> Orders);
public sealed record PaymentMethodsResponse(IReadOnlyList<PaymentMethodResponse> PaymentMethods);

public sealed record OrderResponse(
    int OrderId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Currency,
    string FulfillmentStatus,
    PaymentResponse? Payment,
    IReadOnlyList<OrderLineResponse> Items);

public sealed record OrderLineResponse(
    int CatalogItemId,
    string ProductName,
    decimal UnitPrice,
    int Quantity);

public sealed record PaymentResponse(
    string Status,
    decimal OrderAmount,
    string Currency,
    string InvoiceId,
    string? PayPalOrderId,
    string? FundingBrand,
    string? FundingLastDigits,
    int? SavedPaymentMethodId,
    PaymentAuthorizationResponse? Authorization,
    CaptureResponse? Capture,
    decimal RefundedAmount,
    IReadOnlyList<PaymentRefundResponse> Refunds);

public sealed record PaymentAuthorizationResponse(
    string AuthorizationId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpirationTime);

public sealed record CaptureResponse(
    string CaptureId,
    string Status,
    decimal? Amount,
    decimal? PayPalFee,
    decimal? NetAmount,
    DateTimeOffset? CapturedAt);

public sealed record PaymentRefundResponse(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt);

public sealed record PaymentMethodResponse(
    int PaymentMethodId,
    string Brand,
    string LastDigits,
    string Expiry,
    DateTimeOffset CreatedAt);

public sealed record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<ReconciledPayPalTransaction> PayPalTransactions,
    IReadOnlyList<ReconciledLocalRecord> EShopRecords);

public sealed record ReconciledPayPalTransaction(
    string TransactionId,
    string? ReferenceId,
    string EventCode,
    string Status,
    decimal Amount,
    string Currency,
    decimal? Fee,
    DateTimeOffset InitiatedAt,
    string? InvoiceId,
    int? OrderId,
    string MatchStatus);

public sealed record ReconciledLocalRecord(
    int OrderId,
    string RecordType,
    string ExternalId,
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset RecordedAt,
    string MatchStatus);
