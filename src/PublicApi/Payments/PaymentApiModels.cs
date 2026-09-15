using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderItemRequest> Items, ShippingAddressRequest? ShippingAddress = null);
public sealed record PlaceOrderItemRequest(int CatalogItemId, int Quantity);
public sealed record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
public sealed record PlaceOrderResponse(int OrderId, string PaymentStatus, decimal Total, string Currency);

public sealed record PayOrderRequest(CardDetailsRequest? Card = null, int? PaymentMethodId = null);
public sealed record CardDetailsRequest(string Name, string Number, string Expiry, string SecurityCode,
    BillingAddressRequest BillingAddress);
public sealed record BillingAddressRequest(string CountryCode, string? AddressLine1 = null,
    string? AddressLine2 = null, string? AdminArea1 = null, string? AdminArea2 = null,
    string? PostalCode = null);
public sealed record PaymentResponse(int OrderId, string PaymentStatus, string? PayPalOrderId,
    string? AuthorizationId, string? AuthorizationStatus, decimal? AuthorizedAmount, string Currency);
public sealed record FulfilResponse(int OrderId, string PaymentStatus, string CaptureId, string CaptureStatus,
    decimal CapturedAmount, decimal? PayPalFee, decimal? NetProceeds, string Currency);
public sealed record CancelResponse(int OrderId, string PaymentStatus, string? AuthorizationStatus);
public sealed record RefundOrderRequest(string IdempotencyKey, decimal? Amount = null);
public sealed record RefundResponse(int RefundId, int OrderId, string PayPalRefundId, string Status,
    decimal Amount, string Currency);

public sealed record SavePaymentMethodRequest(CardDetailsRequest Card);
public sealed record PaymentMethodResponse(int PaymentMethodId, string? Brand, string? LastDigits,
    string? Expiry, string? CardType);

public sealed record OrderSummaryResponse(int OrderId, DateTimeOffset OrderDate, string PaymentStatus,
    decimal Total, string? Currency, string? PayPalOrderId, string? AuthorizationId,
    string? AuthorizationStatus, string? CaptureId, string? CaptureStatus, decimal? CapturedAmount,
    decimal? PayPalFee, decimal? NetProceeds, decimal RefundedAmount,
    IReadOnlyList<RefundSummaryResponse> Refunds);
public sealed record RefundSummaryResponse(int RefundId, string? PayPalRefundId, string Status, decimal Amount);

public sealed record ReconciliationResponse(DateTimeOffset From, DateTimeOffset To,
    DateTimeOffset GeneratedAt, IReadOnlyList<ReconciliationRow> Rows);
public sealed record ReconciliationRow(string MatchStatus, int? OrderId, string? ProviderTransactionId,
    string? ProviderReferenceId, string? ProviderStatus, decimal? Amount, string? Currency,
    decimal? Fee, DateTimeOffset? TransactionDate);

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string message, string? providerDebugId = null) : base(message)
    {
        StatusCode = statusCode;
        ProviderDebugId = providerDebugId;
    }

    public int StatusCode { get; }
    public string? ProviderDebugId { get; }
}
